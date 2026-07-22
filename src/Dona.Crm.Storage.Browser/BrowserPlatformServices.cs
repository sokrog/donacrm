using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;
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
        var updated = current with { Settings = new GoogleConnectionSettings(string.Empty, spreadsheetId, driveFolderId, clientId) };
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
            ? new GoogleConnectionState(new GoogleConnectionSettings(string.Empty, string.Empty, null, string.Empty), false)
            : JsonSerializer.Deserialize<GoogleConnectionState>(json, JsonOptions)
                ?? new GoogleConnectionState(new GoogleConnectionSettings(string.Empty, string.Empty, null, string.Empty), false);
    }

    private Task WriteStateAsync(GoogleConnectionState state, CancellationToken cancellationToken) =>
        javascript.InvokeVoidAsync("localStorage.setItem", cancellationToken, StateKey, JsonSerializer.Serialize(state, JsonOptions)).AsTask();
}

public sealed class BrowserProductImagePicker(IJSRuntime javascript) : IProductImagePicker
{
    public async Task<ProductImage?> PickAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var picked = await javascript.InvokeAsync<BrowserPickedImage?>("donaBrowser.pickImage", cancellationToken, 600_000);
        return picked is null ? null : new ProductImage
        {
            FileName = picked.Name,
            ContentType = picked.ContentType,
            SizeBytes = picked.Size,
            Storage = ProductImageStorage.Local,
            StorageKey = $"browser:{productId:N}:{Guid.NewGuid():N}",
            Url = picked.DataUrl
        };
    }

    public Task DeleteAsync(ProductImage image, CancellationToken cancellationToken = default) => Task.CompletedTask;

    private sealed record BrowserPickedImage(string Name, string ContentType, long Size, string DataUrl);
}

public sealed class BrowserGoogleSyncService(
    HttpClient http,
    IJSRuntime javascript,
    IGoogleConnectionService connection,
    IGoogleAccessTokenProvider tokens,
    BrowserCrmRepository local) : IGoogleSyncService
{
    private const string SheetTitle = "_CommandOrbitSync";
    private const string OperationsKey = "dona.crm.google.sync-operations.v1";
    private const int ChunkLength = 40_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<GoogleSyncPreview> PreviewAsync(CancellationToken cancellationToken = default)
    {
        var remote = await ReadRemoteAsync(cancellationToken);
        return Preview(await local.ReadSnapshotAsync(cancellationToken), remote);
    }

    public async Task<GoogleSyncPreview> PullAsync(string expectedGoogleVersion, CancellationToken cancellationToken = default)
    {
        RequirePreview(expectedGoogleVersion);
        var remote = await ReadRemoteAsync(cancellationToken);
        EnsureUnchanged(remote.Version, expectedGoogleVersion);
        await local.ReplaceSnapshotAsync(remote.Snapshot, cancellationToken);
        return Preview(await local.ReadSnapshotAsync(cancellationToken), remote);
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
            return new(operation.Id, operation.LocalVersion, capturedAt, false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            operation.Status = GoogleSyncOperationStatus.RequiresRetry;
            operation.Error = exception.Message;
            await SaveOperationAsync(operation, cancellationToken);
            throw;
        }
    }

    private async Task<GoogleSyncEnvelope> ReadRemoteAsync(CancellationToken cancellationToken)
    {
        var (spreadsheetId, token) = await GetContextAsync(cancellationToken);
        var range = Uri.EscapeDataString($"'{SheetTitle}'!A:C");
        using var response = await SendAsync(HttpMethod.Get,
            $"https://sheets.googleapis.com/v4/spreadsheets/{Uri.EscapeDataString(spreadsheetId)}/values/{range}", token, null, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            return EmptyEnvelope();
        await EnsureSuccessAsync(response, "Не удалось прочитать данные синхронизации", cancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("values", out var values) || values.GetArrayLength() == 0)
            return EmptyEnvelope();
        var rows = values.EnumerateArray().ToArray();
        if (rows[0].GetArrayLength() < 3 || rows[0][0].GetString() != "CommandOrbitSyncV1")
            throw new InvalidOperationException("Скрытый лист синхронизации имеет неизвестный формат.");
        var version = rows[0][1].GetString() ?? string.Empty;
        var capturedAt = DateTimeOffset.Parse(rows[0][2].GetString() ?? string.Empty, System.Globalization.CultureInfo.InvariantCulture);
        var encoded = string.Concat(rows.Skip(1).OrderBy(row => int.Parse(row[0].GetString() ?? "0", System.Globalization.CultureInfo.InvariantCulture)).Select(row => row[1].GetString()));
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        var snapshot = JsonSerializer.Deserialize<DonaSyncSnapshot>(json, JsonOptions)
            ?? throw new InvalidOperationException("Google Sheets вернул пустой снимок данных.");
        return new(version, capturedAt, snapshot);
    }

    private async Task WriteRemoteAsync(GoogleSyncEnvelope envelope, CancellationToken cancellationToken)
    {
        var (spreadsheetId, token) = await GetContextAsync(cancellationToken);
        await EnsureSheetAsync(spreadsheetId, token, cancellationToken);
        var range = Uri.EscapeDataString($"'{SheetTitle}'!A:C");
        using (var clear = await SendAsync(HttpMethod.Post,
            $"https://sheets.googleapis.com/v4/spreadsheets/{Uri.EscapeDataString(spreadsheetId)}/values/{range}:clear", token, new { }, cancellationToken))
            await EnsureSuccessAsync(clear, "Не удалось очистить предыдущий снимок", cancellationToken);

        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope.Snapshot, JsonOptions)));
        var rows = new List<object[]> { new object[] { "CommandOrbitSyncV1", envelope.Version, envelope.CapturedAt.ToString("O") } };
        for (var offset = 0; offset < encoded.Length; offset += ChunkLength)
            rows.Add([(offset / ChunkLength + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), encoded.Substring(offset, Math.Min(ChunkLength, encoded.Length - offset))]);
        var writeRange = Uri.EscapeDataString($"'{SheetTitle}'!A1");
        using var write = await SendAsync(HttpMethod.Put,
            $"https://sheets.googleapis.com/v4/spreadsheets/{Uri.EscapeDataString(spreadsheetId)}/values/{writeRange}?valueInputOption=RAW",
            token, new { range = $"'{SheetTitle}'!A1", majorDimension = "ROWS", values = rows }, cancellationToken);
        await EnsureSuccessAsync(write, "Не удалось записать снимок", cancellationToken);
    }

    private async Task EnsureSheetAsync(string spreadsheetId, string token, CancellationToken cancellationToken)
    {
        var range = Uri.EscapeDataString($"'{SheetTitle}'!A1");
        using var probe = await SendAsync(HttpMethod.Get,
            $"https://sheets.googleapis.com/v4/spreadsheets/{Uri.EscapeDataString(spreadsheetId)}/values/{range}", token, null, cancellationToken);
        if (probe.IsSuccessStatusCode) return;
        using var create = await SendAsync(HttpMethod.Post,
            $"https://sheets.googleapis.com/v4/spreadsheets/{Uri.EscapeDataString(spreadsheetId)}:batchUpdate", token,
            new { requests = new[] { new { addSheet = new { properties = new { title = SheetTitle, hidden = true } } } } }, cancellationToken);
        await EnsureSuccessAsync(create, "Не удалось создать служебный лист синхронизации", cancellationToken);
    }

    private async Task<(string SpreadsheetId, string Token)> GetContextAsync(CancellationToken cancellationToken)
    {
        var state = await connection.GetStateAsync(cancellationToken);
        if (!state.IsConnected || !state.IsConfigured)
            throw new InvalidOperationException("Сначала подключите и проверьте Google в разделе «Подключения».");
        return (state.Settings.SpreadsheetId, await tokens.GetAccessTokenAsync(cancellationToken));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await http.SendAsync(request, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string message, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"{message}: {detail}");
    }

    private async Task SaveOperationAsync(GoogleSyncOperation operation, CancellationToken cancellationToken)
    {
        var operations = (await GetOperationsAsync(cancellationToken)).ToList();
        var index = operations.FindIndex(value => value.Id == operation.Id);
        if (index >= 0) operations[index] = operation; else operations.Add(operation);
        await javascript.InvokeVoidAsync("localStorage.setItem", cancellationToken, OperationsKey, JsonSerializer.Serialize(operations, JsonOptions));
    }

    private static GoogleSyncEnvelope EmptyEnvelope()
    {
        var snapshot = new DonaSyncSnapshot();
        return new(DonaSyncFingerprint.Create(snapshot), DateTimeOffset.MinValue, snapshot);
    }

    private static void RequirePreview(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) throw new InvalidOperationException("Сначала обновите предварительный просмотр.");
    }

    private static void EnsureUnchanged(string actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidOperationException("Google-таблица изменилась после предварительного просмотра. Обновите сравнение и проверьте данные ещё раз.");
    }

    private static GoogleSyncPreview Preview(DonaSyncSnapshot localSnapshot, GoogleSyncEnvelope remote) => new(
        DonaSyncFingerprint.Create(localSnapshot), remote.Version, remote.CapturedAt,
        DonaSyncFingerprint.Compare(localSnapshot, remote.Snapshot));
}
