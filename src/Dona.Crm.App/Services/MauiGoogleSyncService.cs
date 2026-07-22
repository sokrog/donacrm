using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dona.Crm.Storage.Sqlite;
using Dona.Crm.Web.Services;

namespace Dona.Crm.App.Services;

public sealed class MauiGoogleSyncService(
    HttpClient http,
    IGoogleConnectionService connection,
    IGoogleAccessTokenProvider tokens,
    SqliteSyncStore local,
    SqliteSyncOperationStore operations) : IGoogleSyncService
{
    public async Task<GoogleSyncPreview> PreviewAsync(CancellationToken cancellationToken = default)
    {
        var remote = await GetRemoteAsync(cancellationToken);
        var localSnapshot = await local.ReadAsync(cancellationToken);
        return Preview(localSnapshot, remote);
    }

    public async Task<GoogleSyncPreview> PullAsync(string expectedGoogleVersion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expectedGoogleVersion)) throw new InvalidOperationException("Сначала обновите предварительный просмотр.");
        var remote = await GetRemoteAsync(cancellationToken);
        if (!string.Equals(remote.Version, expectedGoogleVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("Google-таблица изменилась после предварительного просмотра. Обновите сравнение и проверьте данные ещё раз.");
        await local.ReplaceAsync(remote.Snapshot, cancellationToken);
        var applied = await local.ReadAsync(cancellationToken);
        return Preview(applied, remote);
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
        var payload = new GoogleSyncPushRequest(state.Settings.SpreadsheetId, operation.Id, operation.ExpectedGoogleVersion, operation.LocalVersion, operation.CreatedAt, operation.Snapshot);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{state.Settings.BrokerBaseUrl}/api/mobile/sync/push");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(payload);
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadProblemAsync(response, cancellationToken));
            var result = await response.Content.ReadFromJsonAsync<GoogleSyncPushResult>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("Сервер вернул пустой результат отправки.");
            operation.Status = GoogleSyncOperationStatus.Applied;
            operation.AppliedAt = result.AppliedAt;
            operation.Error = null;
            await operations.SaveAsync(operation, cancellationToken);
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            operation.Status = GoogleSyncOperationStatus.RequiresRetry;
            operation.Error = exception.Message;
            await operations.SaveAsync(operation, cancellationToken);
            throw;
        }
    }

    private async Task<GoogleSyncEnvelope> GetRemoteAsync(CancellationToken cancellationToken)
    {
        var state = await connection.GetStateAsync(cancellationToken);
        if (!state.IsConnected || !state.IsConfigured) throw new InvalidOperationException("Сначала подключите и проверьте Google в разделе «Подключения».");
        var token = await tokens.GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{state.Settings.BrokerBaseUrl}/api/mobile/sync/snapshot?spreadsheetId={Uri.EscapeDataString(state.Settings.SpreadsheetId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            try
            {
                using var json = JsonDocument.Parse(detail);
                detail = json.RootElement.TryGetProperty("detail", out var value) ? value.GetString() ?? detail : detail;
            }
            catch (JsonException) { }
            throw new InvalidOperationException($"Не удалось получить снимок Google Sheets: {detail}");
        }
        return await response.Content.ReadFromJsonAsync<GoogleSyncEnvelope>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Сервер вернул пустой снимок Google Sheets.");
    }

    private static async Task<string> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var json = JsonDocument.Parse(detail);
            detail = json.RootElement.TryGetProperty("detail", out var value) ? value.GetString() ?? detail : detail;
        }
        catch (JsonException) { }
        return $"Не удалось отправить снимок в Google Sheets: {detail}";
    }

    private static GoogleSyncPreview Preview(DonaSyncSnapshot localSnapshot, GoogleSyncEnvelope remote) => new(
        DonaSyncFingerprint.Create(localSnapshot),
        remote.Version,
        remote.CapturedAt,
        DonaSyncFingerprint.Compare(localSnapshot, remote.Snapshot));
}
