using Dona.Crm.Storage.Sqlite;
using Dona.Crm.Web.Services;

namespace Dona.Crm.App.Services;

public sealed class MauiGoogleSyncService(
    GoogleSheetsSnapshotClient remote,
    IGoogleConnectionService connection,
    IGoogleAccessTokenProvider tokens,
    SqliteSyncStore local,
    SqliteSyncOperationStore operations,
    IGoogleSyncCheckpointStore checkpointStore) : IGoogleSyncService
{
    public async Task<GoogleSyncStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var connectionState = await connection.GetStateAsync(cancellationToken);
        if (!connectionState.IsConnected || !connectionState.IsConfigured)
            return new(GoogleSyncState.LocalOnly);
        var localVersion = DonaSyncFingerprint.Create(await local.ReadAsync(cancellationToken));
        return GoogleSyncStatusEvaluator.Evaluate(true, localVersion, await checkpointStore.ReadAsync(cancellationToken));
    }

    public async Task<GoogleSyncPreview> PreviewAsync(CancellationToken cancellationToken = default)
    {
        var localSnapshot = await local.ReadAsync(cancellationToken);
        var localVersion = DonaSyncFingerprint.Create(localSnapshot);
        try
        {
            var remote = await GetRemoteAsync(cancellationToken);
            var preview = Preview(localSnapshot, remote);
            if (string.Equals(preview.LocalVersion, preview.GoogleVersion, StringComparison.Ordinal))
                await SaveSuccessAsync(preview.LocalVersion, preview.GoogleVersion, DateTimeOffset.UtcNow, cancellationToken);
            else
                await SavePendingAsync(preview.GoogleVersion, cancellationToken);
            return preview;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await SaveFailureAsync(localVersion, exception, cancellationToken);
            throw;
        }
    }

    public async Task<GoogleSyncPreview> PullAsync(string expectedGoogleVersion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expectedGoogleVersion)) throw new InvalidOperationException("Сначала обновите предварительный просмотр.");
        var localVersion = DonaSyncFingerprint.Create(await local.ReadAsync(cancellationToken));
        try
        {
            var remote = await GetRemoteAsync(cancellationToken);
            if (!string.Equals(remote.Version, expectedGoogleVersion, StringComparison.Ordinal))
                throw new GoogleSyncConflictException("Google-таблица изменилась после предварительного просмотра. Обновите сравнение и проверьте данные ещё раз.");
            await local.ReplaceAsync(remote.Snapshot, cancellationToken);
            var applied = await local.ReadAsync(cancellationToken);
            var preview = Preview(applied, remote);
            await SaveSuccessAsync(preview.LocalVersion, remote.Version, DateTimeOffset.UtcNow, cancellationToken);
            return preview;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await SaveFailureAsync(localVersion, exception, cancellationToken);
            throw;
        }
    }

    public async Task<GoogleSyncPushResult> PushAsync(string expectedGoogleVersion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expectedGoogleVersion)) throw new InvalidOperationException("Сначала обновите предварительный просмотр.");
        var snapshot = await local.ReadAsync(cancellationToken);
        var operation = new GoogleSyncOperation
        {
            ExpectedGoogleVersion = expectedGoogleVersion,
            LocalVersion = DonaSyncFingerprint.Create(snapshot),
            Snapshot = snapshot
        };
        await operations.SaveAsync(operation, cancellationToken);
        return await SendAsync(operation, cancellationToken);
    }

    public async Task<GoogleSyncPushResult> RetryPushAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        var operation = (await operations.GetAsync(cancellationToken)).FirstOrDefault(value => value.Id == operationId)
            ?? throw new InvalidOperationException("Операция синхронизации не найдена.");
        if (operation.Status == GoogleSyncOperationStatus.Applied)
            return new(operation.Id, operation.LocalVersion, operation.AppliedAt ?? operation.CreatedAt, true);
        return await SendAsync(operation, cancellationToken);
    }

    public Task<IReadOnlyList<GoogleSyncOperation>> GetOperationsAsync(CancellationToken cancellationToken = default) => operations.GetAsync(cancellationToken);

    private async Task<GoogleSyncPushResult> SendAsync(GoogleSyncOperation operation, CancellationToken cancellationToken)
    {
        var state = await connection.GetStateAsync(cancellationToken);
        if (!state.IsConnected || !state.IsConfigured) throw new InvalidOperationException("Сначала подключите и проверьте Google в разделе «Подключения».");
        var token = await tokens.GetAccessTokenAsync(cancellationToken);
        try
        {
            var current = await remote.ReadAsync(state.Settings.SpreadsheetId, token, cancellationToken);
            if (string.Equals(current.Version, operation.LocalVersion, StringComparison.Ordinal))
            {
                var alreadyApplied = new GoogleSyncPushResult(
                    operation.Id,
                    operation.LocalVersion,
                    current.CapturedAt == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : current.CapturedAt,
                    true);
                operation.Status = GoogleSyncOperationStatus.Applied;
                operation.AppliedAt = alreadyApplied.AppliedAt;
                operation.Error = null;
                await operations.SaveAsync(operation, cancellationToken);
                await SaveSuccessAsync(operation.LocalVersion, current.Version, alreadyApplied.AppliedAt, cancellationToken);
                return alreadyApplied;
            }

            if (!string.Equals(current.Version, operation.ExpectedGoogleVersion, StringComparison.Ordinal))
                throw new GoogleSyncConflictException("Google-таблица изменилась после сравнения. Обновите сравнение и проверьте данные ещё раз.");

            var appliedAt = DateTimeOffset.UtcNow;
            await remote.WriteAsync(
                state.Settings.SpreadsheetId,
                token,
                new GoogleSyncEnvelope(operation.LocalVersion, appliedAt, operation.Snapshot),
                cancellationToken);
            var result = new GoogleSyncPushResult(operation.Id, operation.LocalVersion, appliedAt, false);
            operation.Status = GoogleSyncOperationStatus.Applied;
            operation.AppliedAt = result.AppliedAt;
            operation.Error = null;
            await operations.SaveAsync(operation, cancellationToken);
            await SaveSuccessAsync(operation.LocalVersion, result.Version, result.AppliedAt, cancellationToken);
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            operation.Status = GoogleSyncOperationStatus.RequiresRetry;
            operation.Error = exception.Message;
            await operations.SaveAsync(operation, cancellationToken);
            await SaveFailureAsync(operation.LocalVersion, exception, cancellationToken);
            throw;
        }
    }

    private async Task<GoogleSyncEnvelope> GetRemoteAsync(CancellationToken cancellationToken)
    {
        var state = await connection.GetStateAsync(cancellationToken);
        if (!state.IsConnected || !state.IsConfigured) throw new InvalidOperationException("Сначала подключите и проверьте Google в разделе «Подключения».");
        var token = await tokens.GetAccessTokenAsync(cancellationToken);
        return await remote.ReadAsync(state.Settings.SpreadsheetId, token, cancellationToken);
    }

    private static GoogleSyncPreview Preview(DonaSyncSnapshot localSnapshot, GoogleSyncEnvelope remote) => new(
        DonaSyncFingerprint.Create(localSnapshot),
        remote.Version,
        remote.CapturedAt,
        DonaSyncFingerprint.Compare(localSnapshot, remote.Snapshot));

    private Task SaveSuccessAsync(string localVersion, string googleVersion, DateTimeOffset appliedAt, CancellationToken cancellationToken) =>
        checkpointStore.WriteAsync(new GoogleSyncCheckpoint
        {
            LocalVersion = localVersion,
            GoogleVersion = googleVersion,
            LastAttemptAt = DateTimeOffset.UtcNow,
            LastSuccessfulAt = appliedAt,
            IsPending = false
        }, cancellationToken);

    private async Task SavePendingAsync(string googleVersion, CancellationToken cancellationToken)
    {
        var checkpoint = await checkpointStore.ReadAsync(cancellationToken) ?? new GoogleSyncCheckpoint();
        checkpoint.GoogleVersion = googleVersion;
        checkpoint.LastAttemptAt = DateTimeOffset.UtcNow;
        checkpoint.LastError = null;
        checkpoint.HasConflict = false;
        checkpoint.IsPending = true;
        await checkpointStore.WriteAsync(checkpoint, cancellationToken);
    }

    private async Task SaveFailureAsync(string localVersion, Exception exception, CancellationToken cancellationToken)
    {
        var checkpoint = await checkpointStore.ReadAsync(cancellationToken) ?? new GoogleSyncCheckpoint();
        checkpoint.LocalVersion = localVersion;
        checkpoint.LastAttemptAt = DateTimeOffset.UtcNow;
        checkpoint.LastError = exception.Message;
        checkpoint.HasConflict = exception is GoogleSyncConflictException;
        checkpoint.IsPending = false;
        await checkpointStore.WriteAsync(checkpoint, cancellationToken);
    }
}
