using System.Net.Http.Headers;
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

public sealed class BrowserGoogleSyncService : IGoogleSyncService
{
    private static InvalidOperationException Pending() => new("Прямая синхронизация браузера с Google Sheets будет подключена следующим этапом миграции.");
    public Task<GoogleSyncPreview> PreviewAsync(CancellationToken cancellationToken = default) => Task.FromException<GoogleSyncPreview>(Pending());
    public Task<GoogleSyncPreview> PullAsync(string expectedGoogleVersion, CancellationToken cancellationToken = default) => Task.FromException<GoogleSyncPreview>(Pending());
    public Task<GoogleSyncPushResult> PushAsync(string expectedGoogleVersion, CancellationToken cancellationToken = default) => Task.FromException<GoogleSyncPushResult>(Pending());
    public Task<GoogleSyncPushResult> RetryPushAsync(Guid operationId, CancellationToken cancellationToken = default) => Task.FromException<GoogleSyncPushResult>(Pending());
    public Task<IReadOnlyList<GoogleSyncOperation>> GetOperationsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GoogleSyncOperation>>([]);
}
