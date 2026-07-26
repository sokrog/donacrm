using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dona.Crm.Web.Services;

namespace Dona.Crm.App.Services;

public sealed class MauiGoogleConnectionService(
    HttpClient http,
    ISecureValueStore secure,
    IGooglePlatformAuthorization authorization) : IGoogleConnectionService, IGoogleAccessTokenProvider
{
    private const string SpreadsheetKey = "google.spreadsheet";
    private const string DriveFolderKey = "google.drive-folder";
    private const string OAuthClientIdKey = "google.oauth-client-id";
    private const string AccountKey = "google.account";
    private const string ConnectedKey = "google.connected";
    private const string AccessTokenKey = "google.access-token";
    private const string RefreshTokenKey = "google.refresh-token";
    private const string ExpiresAtKey = "google.expires-at";
    private const string SpreadsheetNameKey = "google.spreadsheet-name";
    private const string DriveFolderNameKey = "google.drive-folder-name";
    private const string LastCheckedKey = "google.last-checked";

    public async Task<GoogleConnectionState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new GoogleConnectionState(
            ReadSettings(),
            string.Equals(await secure.GetAsync(ConnectedKey), "true", StringComparison.Ordinal),
            await secure.GetAsync(AccountKey),
            Preferences.Default.Get<string?>(SpreadsheetNameKey, null),
            Preferences.Default.Get<string?>(DriveFolderNameKey, null),
            ReadDate(LastCheckedKey));
    }

    public async Task<GoogleConnectionState> SaveSettingsAsync(
        GoogleConnectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = Normalize(settings);
        Preferences.Default.Set(SpreadsheetKey, normalized.SpreadsheetId);
        if (normalized.DriveFolderId is null)
            Preferences.Default.Remove(DriveFolderKey);
        else
            Preferences.Default.Set(DriveFolderKey, normalized.DriveFolderId);
        if (string.IsNullOrWhiteSpace(normalized.OAuthClientId))
            Preferences.Default.Remove(OAuthClientIdKey);
        else
            Preferences.Default.Set(OAuthClientIdKey, normalized.OAuthClientId);
        ClearCheckDetails();
        return await GetStateAsync(cancellationToken);
    }

    public async Task<GoogleConnectionState> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var settings = ReadSettings();
        EnsureConfigured(settings);
        var token = await authorization.AuthorizeAsync(
            settings.OAuthClientId,
            interactive: true,
            refreshToken: null,
            cancellationToken);
        await SaveTokenAsync(token);
        await secure.SetAsync(ConnectedKey, "true");
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
        else
        {
            Preferences.Default.Remove(DriveFolderNameKey);
        }

        var user = await GetGoogleAsync<UserInfoResponse>(
            "https://www.googleapis.com/oauth2/v3/userinfo",
            token,
            "Не удалось получить профиль Google",
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(user.Email))
            await secure.SetAsync(AccountKey, user.Email);
        await secure.SetAsync(ConnectedKey, "true");
        Preferences.Default.Set(LastCheckedKey, DateTimeOffset.UtcNow.ToString("O"));
        return await GetStateAsync(cancellationToken);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var accessToken = await secure.GetAsync(AccessTokenKey);
        try
        {
            await authorization.DisconnectAsync(accessToken, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // A local disconnect must remain possible while Google is unavailable.
        }

        secure.Remove(AccessTokenKey);
        secure.Remove(RefreshTokenKey);
        secure.Remove(AccountKey);
        secure.Remove(ConnectedKey);
        secure.Remove(ExpiresAtKey);
        ClearCheckDetails();
    }

    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
        GetAccessTokenAsync(ReadSettings(), cancellationToken);

    private async Task<string> GetAccessTokenAsync(
        GoogleConnectionSettings settings,
        CancellationToken cancellationToken)
    {
        var accessToken = await secure.GetAsync(AccessTokenKey);
        var expires = await secure.GetAsync(ExpiresAtKey);
        if (!string.IsNullOrWhiteSpace(accessToken) &&
            DateTimeOffset.TryParse(expires, out var expiresAt) &&
            expiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return accessToken;
        }

        var refreshed = await authorization.AuthorizeAsync(
            settings.OAuthClientId,
            interactive: false,
            await secure.GetAsync(RefreshTokenKey),
            cancellationToken);
        await SaveTokenAsync(refreshed);
        return refreshed.AccessToken;
    }

    private async Task SaveTokenAsync(GooglePlatformToken token)
    {
        await secure.SetAsync(AccessTokenKey, token.AccessToken);
        await secure.SetAsync(ExpiresAtKey, token.ExpiresAt.ToString("O"));
        if (!string.IsNullOrWhiteSpace(token.RefreshToken))
            await secure.SetAsync(RefreshTokenKey, token.RefreshToken);
    }

    private async Task<T> GetGoogleAsync<T>(
        string url,
        string token,
        string error,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, error, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Google вернул пустой ответ.");
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string prefix,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var json = JsonDocument.Parse(detail);
            detail = json.RootElement.TryGetProperty("error_description", out var description)
                ? description.GetString()
                : json.RootElement.TryGetProperty("error", out var error) &&
                  error.ValueKind == JsonValueKind.Object &&
                  error.TryGetProperty("message", out var message)
                    ? message.GetString()
                    : detail;
        }
        catch (JsonException)
        {
        }
        throw new InvalidOperationException($"{prefix}: {detail ?? response.ReasonPhrase}");
    }

    private static GoogleConnectionSettings Normalize(GoogleConnectionSettings settings)
    {
        var spreadsheet = GoogleResourceIds.Spreadsheet(settings.SpreadsheetId);
        if (string.IsNullOrWhiteSpace(spreadsheet))
            throw new InvalidOperationException("Укажите корректную ссылку или ID Google-таблицы.");
        var folder = GoogleResourceIds.DriveFolder(settings.DriveFolderId);
        if (folder == string.Empty)
            throw new InvalidOperationException("Укажите корректную ссылку или ID папки Google Drive.");
        return new GoogleConnectionSettings(spreadsheet, folder, settings.OAuthClientId?.Trim());
    }

    private void EnsureConfigured(GoogleConnectionSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SpreadsheetId))
            throw new InvalidOperationException("Сначала сохраните Google-таблицу.");
        if (authorization.RequiresClientId &&
            (string.IsNullOrWhiteSpace(settings.OAuthClientId) ||
             !settings.OAuthClientId.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Укажите OAuth Client ID для этой платформы.");
        }
    }

    private static GoogleConnectionSettings ReadSettings() => new(
        Preferences.Default.Get(SpreadsheetKey, string.Empty),
        Preferences.Default.Get<string?>(DriveFolderKey, null),
        Preferences.Default.Get<string?>(OAuthClientIdKey, null));

    private static DateTimeOffset? ReadDate(string key) =>
        DateTimeOffset.TryParse(Preferences.Default.Get<string?>(key, null), out var value) ? value : null;

    private static void ClearCheckDetails()
    {
        Preferences.Default.Remove(SpreadsheetNameKey);
        Preferences.Default.Remove(DriveFolderNameKey);
        Preferences.Default.Remove(LastCheckedKey);
    }

    private sealed record SheetResponse(SheetProperties? Properties);
    private sealed record SheetProperties(string? Title);
    private sealed record DriveFileResponse(string? Name, string? MimeType);
    private sealed record UserInfoResponse(string? Email);
}
