using System.Net.Http.Headers;
using System.Text.Json;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;
using Dona.Crm.Web.Storage;
using Microsoft.JSInterop;

namespace Dona.Crm.Storage.Browser;

public sealed class BrowserGoogleConnectionService(IJSRuntime javascript, HttpClient http) : IGoogleConnectionService, IGoogleAccessTokenProvider
{
    private const string StateKey = "dona.crm.google.connection.v1";
    private const string TokenKey = "dona.crm.google.access-token";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<GoogleConnectionState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var stored = await ReadStateAsync(cancellationToken);
        var token = await javascript.InvokeAsync<string?>("sessionStorage.getItem", cancellationToken, TokenKey);
        return stored with { IsConnected = !string.IsNullOrWhiteSpace(token) };
    }

    public async Task<GoogleConnectionState> SaveSettingsAsync(GoogleConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        var spreadsheetId = GoogleResourceIds.Spreadsheet(settings.SpreadsheetId);
        var driveFolderId = GoogleResourceIds.DriveFolder(settings.DriveFolderId);
        var clientId = settings.OAuthClientId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(spreadsheetId)) throw new InvalidOperationException("Укажите корректную ссылку или ID Google-таблицы.");
        if (driveFolderId == string.Empty) throw new InvalidOperationException("Укажите корректную ссылку или ID папки Google Drive.");
        if (!clientId.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Укажите Web Client ID из Google Cloud Console.");

        var current = await GetStateAsync(cancellationToken);
        var updated = current with { Settings = new GoogleConnectionSettings(spreadsheetId, driveFolderId, clientId) };
        await WriteStateAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<GoogleConnectionState> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var state = await GetStateAsync(cancellationToken);
        if (!state.IsConfigured) throw new InvalidOperationException("Сначала сохраните ID таблицы и Google OAuth Client ID.");
        var token = await javascript.InvokeAsync<string>("donaGoogle.authorize", cancellationToken, state.Settings.OAuthClientId);
        await javascript.InvokeVoidAsync("sessionStorage.setItem", cancellationToken, TokenKey, token);
        return await CheckAsync(cancellationToken);
    }

    public async Task<GoogleConnectionState> CheckAsync(CancellationToken cancellationToken = default)
    {
        var state = await GetStateAsync(cancellationToken);
        var token = await GetAccessTokenAsync(cancellationToken);
        var spreadsheet = await GetJsonAsync($"https://sheets.googleapis.com/v4/spreadsheets/{Uri.EscapeDataString(state.Settings.SpreadsheetId)}?fields=properties.title", token, cancellationToken);
        var spreadsheetName = spreadsheet.GetProperty("properties").GetProperty("title").GetString();
        string? driveFolderName = null;
        if (!string.IsNullOrWhiteSpace(state.Settings.DriveFolderId))
        {
            var folder = await GetJsonAsync($"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(state.Settings.DriveFolderId)}?fields=id,name,mimeType&supportsAllDrives=true", token, cancellationToken);
            driveFolderName = folder.GetProperty("name").GetString();
        }
        var user = await GetJsonAsync("https://www.googleapis.com/oauth2/v3/userinfo", token, cancellationToken);
        var updated = state with
        {
            IsConnected = true,
            AccountEmail = user.TryGetProperty("email", out var email) ? email.GetString() : null,
            SpreadsheetName = spreadsheetName,
            DriveFolderName = driveFolderName,
            LastCheckedAt = DateTimeOffset.UtcNow
        };
        await WriteStateAsync(updated, cancellationToken);
        return updated;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var token = await javascript.InvokeAsync<string?>("sessionStorage.getItem", cancellationToken, TokenKey);
        if (!string.IsNullOrWhiteSpace(token)) await javascript.InvokeVoidAsync("donaGoogle.revoke", cancellationToken, token);
        await javascript.InvokeVoidAsync("sessionStorage.removeItem", cancellationToken, TokenKey);
        var state = await ReadStateAsync(cancellationToken);
        await WriteStateAsync(state with { IsConnected = false, AccountEmail = null }, cancellationToken);
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
        await javascript.InvokeAsync<string?>("sessionStorage.getItem", cancellationToken, TokenKey)
        ?? throw new InvalidOperationException("Сессия Google завершена. Выполните вход ещё раз.");

    private async Task<JsonElement> GetJsonAsync(string url, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Google не подтвердил доступ: {detail}");
        }
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.Clone();
    }

    private async Task<GoogleConnectionState> ReadStateAsync(CancellationToken cancellationToken)
    {
        var json = await javascript.InvokeAsync<string?>("localStorage.getItem", cancellationToken, StateKey);
        return string.IsNullOrWhiteSpace(json)
            ? new GoogleConnectionState(new GoogleConnectionSettings(string.Empty, null, string.Empty), false)
            : JsonSerializer.Deserialize<GoogleConnectionState>(json, JsonOptions)
                ?? new GoogleConnectionState(new GoogleConnectionSettings(string.Empty, null, string.Empty), false);
    }

    private Task WriteStateAsync(GoogleConnectionState state, CancellationToken cancellationToken) =>
        javascript.InvokeVoidAsync("localStorage.setItem", cancellationToken, StateKey, JsonSerializer.Serialize(state, JsonOptions)).AsTask();
}

public sealed class BrowserProductImagePicker(
    IJSRuntime javascript,
    IBusinessSettingsRepository businessSettings,
    IGoogleConnectionService google,
    IGoogleAccessTokenProvider tokens,
    GoogleDriveFileClient drive) : IProductImagePicker
{
    public async Task<ProductImage?> PickAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var picked = await javascript.InvokeAsync<BrowserPickedImage?>("donaBrowser.pickImage", cancellationToken, 600_000);
        if (picked is null)
            return null;

        var settings = await businessSettings.GetAsync(cancellationToken);
        if (settings.UseGoogleDriveImages)
        {
            var state = await google.GetStateAsync(cancellationToken);
            if (!state.IsConnected)
                throw new InvalidOperationException("Сначала подключите Google в разделе «Подключения».");
            var separator = picked.DataUrl.IndexOf(',');
            if (separator < 0)
                throw new InvalidDataException("Браузер вернул повреждённое изображение.");
            var bytes = Convert.FromBase64String(picked.DataUrl[(separator + 1)..]);
            var uploaded = await drive.UploadAsync(
                state.Settings.DriveFolderId,
                picked.Name,
                picked.ContentType,
                bytes,
                await tokens.GetAccessTokenAsync(cancellationToken),
                cancellationToken);
            return new ProductImage
            {
                FileName = picked.Name,
                ContentType = uploaded.MimeType,
                SizeBytes = uploaded.Size,
                Storage = ProductImageStorage.GoogleDrive,
                StorageKey = uploaded.Id,
                Url = $"drive:{uploaded.Id}"
            };
        }

        return new ProductImage
        {
            FileName = picked.Name,
            ContentType = picked.ContentType,
            SizeBytes = picked.Size,
            Storage = ProductImageStorage.Local,
            StorageKey = $"browser:{productId:N}:{Guid.NewGuid():N}",
            Url = picked.DataUrl
        };
    }

    public async Task DeleteAsync(ProductImage image, CancellationToken cancellationToken = default)
    {
        if (image.Storage != ProductImageStorage.GoogleDrive || string.IsNullOrWhiteSpace(image.StorageKey))
            return;
        await drive.DeleteAsync(
            image.StorageKey,
            await tokens.GetAccessTokenAsync(cancellationToken),
            cancellationToken);
    }

    private sealed record BrowserPickedImage(string Name, string ContentType, long Size, string DataUrl);
}

public sealed class BrowserGoogleSyncService(
    GoogleSheetsSnapshotClient remote,
    IJSRuntime javascript,
    IGoogleConnectionService connection,
    IGoogleAccessTokenProvider tokens,
    BrowserCrmRepository local,
    IGoogleSyncCheckpointStore checkpointStore) : IGoogleSyncService
{
    private const string OperationsKey = "dona.crm.google.sync-operations.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<GoogleSyncStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var connectionState = await connection.GetStateAsync(cancellationToken);
        if (!connectionState.IsConnected || !connectionState.IsConfigured)
            return new(GoogleSyncState.LocalOnly);
        var localVersion = DonaSyncFingerprint.Create(await local.ReadSnapshotAsync(cancellationToken));
        return GoogleSyncStatusEvaluator.Evaluate(true, localVersion, await checkpointStore.ReadAsync(cancellationToken));
    }

    public async Task<GoogleSyncPreview> PreviewAsync(CancellationToken cancellationToken = default)
    {
        var localSnapshot = await local.ReadSnapshotAsync(cancellationToken);
        var localVersion = DonaSyncFingerprint.Create(localSnapshot);
        try
        {
            var remote = await ReadRemoteAsync(cancellationToken);
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
        RequirePreview(expectedGoogleVersion);
        var localVersion = DonaSyncFingerprint.Create(await local.ReadSnapshotAsync(cancellationToken));
        try
        {
            var remote = await ReadRemoteAsync(cancellationToken);
            EnsureUnchanged(remote.Version, expectedGoogleVersion);
            await local.ReplaceSnapshotAsync(remote.Snapshot, cancellationToken);
            var preview = Preview(await local.ReadSnapshotAsync(cancellationToken), remote);
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
        RequirePreview(expectedGoogleVersion);
        var snapshot = await local.ReadSnapshotAsync(cancellationToken);
        var operation = new GoogleSyncOperation
        {
            ExpectedGoogleVersion = expectedGoogleVersion,
            LocalVersion = DonaSyncFingerprint.Create(snapshot),
            Snapshot = snapshot
        };
        await SaveOperationAsync(operation, cancellationToken);
        return await SendAsync(operation, cancellationToken);
    }

    public async Task<GoogleSyncPushResult> RetryPushAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        var operation = (await GetOperationsAsync(cancellationToken)).FirstOrDefault(value => value.Id == operationId)
            ?? throw new InvalidOperationException("Операция синхронизации не найдена.");
        if (operation.Status == GoogleSyncOperationStatus.Applied)
            return new(operation.Id, operation.LocalVersion, operation.AppliedAt ?? operation.CreatedAt, true);
        return await SendAsync(operation, cancellationToken);
    }

    public async Task<IReadOnlyList<GoogleSyncOperation>> GetOperationsAsync(CancellationToken cancellationToken = default)
    {
        var json = await javascript.InvokeAsync<string?>("localStorage.getItem", cancellationToken, OperationsKey);
        return string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<GoogleSyncOperation>>(json, JsonOptions) ?? [];
    }

    private async Task<GoogleSyncPushResult> SendAsync(GoogleSyncOperation operation, CancellationToken cancellationToken)
    {
        try
        {
            var current = await ReadRemoteAsync(cancellationToken);
            EnsureUnchanged(current.Version, operation.ExpectedGoogleVersion);
            var capturedAt = DateTimeOffset.UtcNow;
            await WriteRemoteAsync(new GoogleSyncEnvelope(operation.LocalVersion, capturedAt, operation.Snapshot), cancellationToken);
            operation.Status = GoogleSyncOperationStatus.Applied;
            operation.AppliedAt = capturedAt;
            operation.Error = null;
            await SaveOperationAsync(operation, cancellationToken);
            await SaveSuccessAsync(operation.LocalVersion, operation.LocalVersion, capturedAt, cancellationToken);
            return new(operation.Id, operation.LocalVersion, capturedAt, false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            operation.Status = GoogleSyncOperationStatus.RequiresRetry;
            operation.Error = exception.Message;
            await SaveOperationAsync(operation, cancellationToken);
            await SaveFailureAsync(operation.LocalVersion, exception, cancellationToken);
            throw;
        }
    }

    private async Task<GoogleSyncEnvelope> ReadRemoteAsync(CancellationToken cancellationToken)
    {
        var (spreadsheetId, token) = await GetContextAsync(cancellationToken);
        return await remote.ReadAsync(spreadsheetId, token, cancellationToken);
    }

    private async Task WriteRemoteAsync(GoogleSyncEnvelope envelope, CancellationToken cancellationToken)
    {
        var (spreadsheetId, token) = await GetContextAsync(cancellationToken);
        await remote.WriteAsync(spreadsheetId, token, envelope, cancellationToken);
    }

    private async Task<(string SpreadsheetId, string Token)> GetContextAsync(CancellationToken cancellationToken)
    {
        var state = await connection.GetStateAsync(cancellationToken);
        if (!state.IsConnected || !state.IsConfigured)
            throw new InvalidOperationException("Сначала подключите и проверьте Google в разделе «Подключения».");
        return (state.Settings.SpreadsheetId, await tokens.GetAccessTokenAsync(cancellationToken));
    }

    private async Task SaveOperationAsync(GoogleSyncOperation operation, CancellationToken cancellationToken)
    {
        var operations = (await GetOperationsAsync(cancellationToken)).ToList();
        var index = operations.FindIndex(value => value.Id == operation.Id);
        if (index >= 0) operations[index] = operation; else operations.Add(operation);
        await javascript.InvokeVoidAsync("localStorage.setItem", cancellationToken, OperationsKey, JsonSerializer.Serialize(operations, JsonOptions));
    }

    private static void RequirePreview(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) throw new InvalidOperationException("Сначала обновите предварительный просмотр.");
    }

    private static void EnsureUnchanged(string actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new GoogleSyncConflictException("Google-таблица изменилась после предварительного просмотра. Обновите сравнение и проверьте данные ещё раз.");
    }

    private static GoogleSyncPreview Preview(DonaSyncSnapshot localSnapshot, GoogleSyncEnvelope remote) => new(
        DonaSyncFingerprint.Create(localSnapshot), remote.Version, remote.CapturedAt,
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

public sealed class BrowserGoogleSyncCheckpointStore(IJSRuntime javascript) : IGoogleSyncCheckpointStore
{
    private const string StorageKey = "dona.crm.google.sync-checkpoint.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<GoogleSyncCheckpoint?> ReadAsync(CancellationToken cancellationToken = default)
    {
        var json = await javascript.InvokeAsync<string?>("localStorage.getItem", cancellationToken, StorageKey);
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<GoogleSyncCheckpoint>(json, JsonOptions);
    }

    public async Task WriteAsync(GoogleSyncCheckpoint checkpoint, CancellationToken cancellationToken = default) =>
        await javascript.InvokeVoidAsync("localStorage.setItem", cancellationToken, StorageKey, JsonSerializer.Serialize(checkpoint, JsonOptions));
}
