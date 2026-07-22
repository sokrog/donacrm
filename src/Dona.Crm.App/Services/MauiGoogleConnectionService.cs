using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dona.Crm.Web.Services;

namespace Dona.Crm.App.Services;

public sealed class MauiGoogleConnectionService(HttpClient http, ISecureValueStore secure) : IGoogleConnectionService
{
    private const string BrokerKey = "google.broker";
    private const string SpreadsheetKey = "google.spreadsheet";
    private const string DriveFolderKey = "google.drive-folder";
    private const string AccountKey = "google.account";
    private const string AccessTokenKey = "google.access-token";
    private const string RefreshTokenKey = "google.refresh-token";
    private const string ExpiresAtKey = "google.expires-at";
    private const string SpreadsheetNameKey = "google.spreadsheet-name";
    private const string DriveFolderNameKey = "google.drive-folder-name";
    private const string LastCheckedKey = "google.last-checked";
    private static readonly Uri CallbackUrl = new("donacrm://oauth2redirect");

    public async Task<GoogleConnectionState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = ReadSettings();
        return new GoogleConnectionState(
            settings,
            !string.IsNullOrWhiteSpace(await secure.GetAsync(RefreshTokenKey)),
            await secure.GetAsync(AccountKey),
            Preferences.Default.Get<string?>(SpreadsheetNameKey, null),
            Preferences.Default.Get<string?>(DriveFolderNameKey, null),
            ReadDate(LastCheckedKey));
    }

    public async Task<GoogleConnectionState> SaveSettingsAsync(GoogleConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = Normalize(settings);
        Preferences.Default.Set(BrokerKey, normalized.BrokerBaseUrl);
        Preferences.Default.Set(SpreadsheetKey, normalized.SpreadsheetId);
        if (normalized.DriveFolderId is null) Preferences.Default.Remove(DriveFolderKey);
        else Preferences.Default.Set(DriveFolderKey, normalized.DriveFolderId);
        ClearCheckDetails();
        return await GetStateAsync(cancellationToken);
    }

    public async Task<GoogleConnectionState> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var settings = ReadSettings();
        EnsureConfigured(settings);
        var start = new Uri($"{settings.BrokerBaseUrl}/api/mobile/google/start?callback={Uri.EscapeDataString(CallbackUrl.ToString())}");
        WebAuthenticatorResult result;
        try
        {
            result = await WebAuthenticator.Default.AuthenticateAsync(new WebAuthenticatorOptions
            {
                Url = start,
                CallbackUrl = CallbackUrl,
                PrefersEphemeralWebBrowserSession = false
            });
        }
        catch (TaskCanceledException)
        {
            throw new InvalidOperationException("Подключение отменено.");
        }

        if (!result.Properties.TryGetValue("grant", out var grant) || string.IsNullOrWhiteSpace(grant))
            throw new InvalidOperationException("Сервер авторизации не вернул одноразовый код.");

        var response = await http.PostAsJsonAsync($"{settings.BrokerBaseUrl}/api/mobile/google/exchange", new { grant }, cancellationToken);
        await EnsureSuccessAsync(response, "Не удалось завершить вход в Google", cancellationToken);
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Сервер авторизации вернул пустой ответ.");
        if (string.IsNullOrWhiteSpace(token.RefreshToken)) throw new InvalidOperationException("Google не выдал refresh token. Отключите доступ DONA CRM в аккаунте Google и повторите вход.");

        await secure.SetAsync(AccessTokenKey, token.AccessToken);
        await secure.SetAsync(RefreshTokenKey, token.RefreshToken);
        await secure.SetAsync(AccountKey, token.Email);
        await secure.SetAsync(ExpiresAtKey, DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn).ToString("O"));
        return await CheckAsync(cancellationToken);
    }

    public async Task<GoogleConnectionState> CheckAsync(CancellationToken cancellationToken = default)
    {
        var settings = ReadSettings();
        EnsureConfigured(settings);
        var token = await GetAccessTokenAsync(settings, cancellationToken);
        var sheet = await GetGoogleAsync<SheetResponse>(
            $"https://sheets.googleapis.com/v4/spreadsheets/{Uri.EscapeDataString(settings.SpreadsheetId)}?fields=spreadsheetId,properties.title",
            token,
            "Не удалось открыть Google-таблицу",
            cancellationToken);
        Preferences.Default.Set(SpreadsheetNameKey, sheet.Properties?.Title ?? settings.SpreadsheetId);

        if (settings.DriveFolderId is not null)
        {
            var folder = await GetGoogleAsync<DriveFileResponse>(
                $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(settings.DriveFolderId)}?fields=id,name,mimeType&supportsAllDrives=true",
                token,
                "Не удалось открыть папку Google Drive",
                cancellationToken);
            if (!string.Equals(folder.MimeType, "application/vnd.google-apps.folder", StringComparison.Ordinal))
                throw new InvalidOperationException("Указанный объект Google Drive не является папкой.");
            Preferences.Default.Set(DriveFolderNameKey, folder.Name ?? settings.DriveFolderId);
        }
        else Preferences.Default.Remove(DriveFolderNameKey);

        Preferences.Default.Set(LastCheckedKey, DateTimeOffset.UtcNow.ToString("O"));
        return await GetStateAsync(cancellationToken);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        secure.Remove(AccessTokenKey);
        secure.Remove(RefreshTokenKey);
        secure.Remove(AccountKey);
        secure.Remove(ExpiresAtKey);
        ClearCheckDetails();
        return Task.CompletedTask;
    }

    private async Task<string> GetAccessTokenAsync(GoogleConnectionSettings settings, CancellationToken token)
    {
        var accessToken = await secure.GetAsync(AccessTokenKey);
        var expires = await secure.GetAsync(ExpiresAtKey);
        if (!string.IsNullOrWhiteSpace(accessToken) && DateTimeOffset.TryParse(expires, out var expiresAt) && expiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            return accessToken;

        var refreshToken = await secure.GetAsync(RefreshTokenKey);
        if (string.IsNullOrWhiteSpace(refreshToken)) throw new InvalidOperationException("Сначала войдите в Google.");
        var response = await http.PostAsJsonAsync($"{settings.BrokerBaseUrl}/api/mobile/google/refresh", new { refreshToken }, token);
        await EnsureSuccessAsync(response, "Не удалось обновить доступ Google", token);
        var refreshed = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: token)
            ?? throw new InvalidOperationException("Сервер авторизации вернул пустой ответ.");
        await secure.SetAsync(AccessTokenKey, refreshed.AccessToken);
        await secure.SetAsync(ExpiresAtKey, DateTimeOffset.UtcNow.AddSeconds(refreshed.ExpiresIn).ToString("O"));
        return refreshed.AccessToken;
    }

    private async Task<T> GetGoogleAsync<T>(string url, string token, string error, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, error, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Google вернул пустой ответ.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string prefix, CancellationToken token)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync(token);
        try
        {
            using var json = JsonDocument.Parse(detail);
            detail = json.RootElement.TryGetProperty("error_description", out var description) ? description.GetString()
                : json.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message) ? message.GetString()
                : detail;
        }
        catch (JsonException) { }
        throw new InvalidOperationException($"{prefix}: {detail ?? response.ReasonPhrase}");
    }

    private static GoogleConnectionSettings Normalize(GoogleConnectionSettings settings)
    {
        var broker = GoogleResourceIds.BrokerBaseUrl(settings.BrokerBaseUrl);
        if (string.IsNullOrWhiteSpace(broker)) throw new InvalidOperationException("Укажите HTTPS-адрес сервера DONA CRM.");
        var spreadsheet = GoogleResourceIds.Spreadsheet(settings.SpreadsheetId);
        if (string.IsNullOrWhiteSpace(spreadsheet)) throw new InvalidOperationException("Укажите корректную ссылку или ID Google-таблицы.");
        var folder = GoogleResourceIds.DriveFolder(settings.DriveFolderId);
        if (folder == string.Empty) throw new InvalidOperationException("Укажите корректную ссылку или ID папки Google Drive.");
        return new GoogleConnectionSettings(broker, spreadsheet, folder);
    }

    private static void EnsureConfigured(GoogleConnectionSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.BrokerBaseUrl) || string.IsNullOrWhiteSpace(settings.SpreadsheetId))
            throw new InvalidOperationException("Сначала сохраните адрес сервера и Google-таблицу.");
    }

    private static GoogleConnectionSettings ReadSettings() => new(
        Preferences.Default.Get(BrokerKey, string.Empty),
        Preferences.Default.Get(SpreadsheetKey, string.Empty),
        Preferences.Default.Get<string?>(DriveFolderKey, null));

    private static DateTimeOffset? ReadDate(string key) => DateTimeOffset.TryParse(Preferences.Default.Get<string?>(key, null), out var value) ? value : null;

    private static void ClearCheckDetails()
    {
        Preferences.Default.Remove(SpreadsheetNameKey);
        Preferences.Default.Remove(DriveFolderNameKey);
        Preferences.Default.Remove(LastCheckedKey);
    }

    private sealed record TokenResponse(string AccessToken, string RefreshToken, int ExpiresIn, string Email);
    private sealed record SheetResponse(SheetProperties? Properties);
    private sealed record SheetProperties(string? Title);
    private sealed record DriveFileResponse(string? Name, string? MimeType);
}
