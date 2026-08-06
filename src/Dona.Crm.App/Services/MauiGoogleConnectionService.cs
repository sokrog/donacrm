using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dona.Crm.Web.Services;

namespace Dona.Crm.App.Services;

public sealed class MauiGoogleConnectionService(
    HttpClient http,
    ISecureValueStore secure,
    IGooglePlatformAuthorization authorization) : IGoogleConnectionService, IGoogleAccessTokenProvider
{
    private const string SpreadsheetKey = "google.spreadsheet";
    private const string DriveFolderKey = "google.drive-folder";
    private const string AccountKey = "google.account";
    private const string AccountNameKey = "google.account-name";
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
            await secure.GetAsync(AccountNameKey),
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
        ClearCheckDetails();
        return await GetStateAsync(cancellationToken);
    }

    public async Task<GoogleConnectionState> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var settings = ReadSettings();
        EnsureConfigured(settings);
        var token = await authorization.AuthorizeAsync(
            settings.OAuthClientId,
            clientSecret: null,
            interactive: true,
            refreshToken: null,
            cancellationToken);
        await SaveTokenAsync(token);
        return await CheckAsync(cancellationToken);
    }

    public async Task<GoogleConnectionState> CheckAsync(CancellationToken cancellationToken = default)
    {
        var settings = ReadSettings();
        EnsureConfigured(settings);
        var token = await GetAccessTokenAsync(settings, cancellationToken);
        Preferences.Default.Remove(SpreadsheetNameKey);
        Preferences.Default.Remove(DriveFolderNameKey);

        var user = await GetGoogleAsync<UserInfoResponse>(
            "https://www.googleapis.com/oauth2/v3/userinfo",
            token,
            "Не удалось получить профиль Google",
            cancellationToken);
        if (string.IsNullOrWhiteSpace(user.Email))
            throw new InvalidOperationException("Google не вернул email авторизованного аккаунта. Отключите аккаунт и выполните вход снова.");

        await secure.SetAsync(AccountKey, user.Email);
        if (string.IsNullOrWhiteSpace(user.Name))
            secure.Remove(AccountNameKey);
        else
            await secure.SetAsync(AccountNameKey, user.Name);
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
        secure.Remove(AccountNameKey);
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
            clientSecret: null,
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
        if (detail?.Contains("insufficient authentication scopes", StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new InvalidOperationException(
                $"{prefix}: аккаунт не выдал разрешение Google Таблицы. Повторите вход и отметьте разрешения для Google Таблиц и Google Drive.");
        }
        throw new InvalidOperationException($"{prefix}: {detail ?? response.ReasonPhrase}");
    }

    private static GoogleConnectionSettings Normalize(GoogleConnectionSettings settings)
    {
        var rawSpreadsheet = settings.SpreadsheetId?.Trim() ?? string.Empty;
        var spreadsheet = string.IsNullOrWhiteSpace(rawSpreadsheet) ? string.Empty : GoogleResourceIds.Spreadsheet(rawSpreadsheet);
        if (!string.IsNullOrWhiteSpace(rawSpreadsheet) && string.IsNullOrWhiteSpace(spreadsheet))
            throw new InvalidOperationException("Укажите корректную ссылку или ID Google-таблицы.");
        var folder = GoogleResourceIds.DriveFolder(settings.DriveFolderId);
        if (folder == string.Empty)
            throw new InvalidOperationException("Укажите корректную ссылку или ID папки Google Drive.");
        return new GoogleConnectionSettings(
            spreadsheet,
            folder,
            settings.OAuthClientId?.Trim(),
            settings.OAuthClientSecret?.Trim());
    }

    private void EnsureConfigured(GoogleConnectionSettings settings)
    {
        if (authorization.RequiresClientId &&
            (string.IsNullOrWhiteSpace(settings.OAuthClientId) ||
             !settings.OAuthClientId.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Укажите OAuth Client ID для этой платформы.");
        }
    }

    private static GoogleConnectionSettings ReadSettings() => new(
        Preferences.Default.Get(SpreadsheetKey, string.Empty),
        Preferences.Default.Get<string?>(DriveFolderKey, null));

    private static DateTimeOffset? ReadDate(string key) =>
        DateTimeOffset.TryParse(Preferences.Default.Get<string?>(key, null), out var value) ? value : null;

    private static void ClearCheckDetails()
    {
        Preferences.Default.Remove(SpreadsheetNameKey);
        Preferences.Default.Remove(DriveFolderNameKey);
        Preferences.Default.Remove(LastCheckedKey);
    }

    private sealed record UserInfoResponse(
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("name")] string? Name);
}
