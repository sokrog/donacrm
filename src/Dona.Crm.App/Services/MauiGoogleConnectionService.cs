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
    private const string AccountKey = "google.account";
    private const string AccountNameKey = "google.account-name";
    private const string ConnectedKey = "google.connected";
    private const string AccessTokenKey = "google.access-token";
    private const string RefreshTokenKey = "google.refresh-token";
    private const string ExpiresAtKey = "google.expires-at";
    private const string LastCheckedKey = "google.last-checked";
    private static readonly string[] LegacyPreferenceKeys =
    [
        "google.spreadsheet",
        "google.drive-folder",
        "google.spreadsheet-name",
        "google.drive-folder-name"
    ];

    public async Task<GoogleConnectionState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RemoveLegacyPreferences();
        return new GoogleConnectionState(
            ReadSettings(),
            string.Equals(await secure.GetAsync(ConnectedKey), "true", StringComparison.Ordinal),
            await secure.GetAsync(AccountKey),
            await secure.GetAsync(AccountNameKey),
            ReadDate(LastCheckedKey));
    }

    public async Task<GoogleConnectionState> SaveSettingsAsync(
        GoogleConnectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
        throw await GoogleErrorMessages.CreateExceptionAsync(response, prefix, cancellationToken);
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

    private static GoogleConnectionSettings ReadSettings() => new();

    private static DateTimeOffset? ReadDate(string key) =>
        DateTimeOffset.TryParse(Preferences.Default.Get<string?>(key, null), out var value) ? value : null;

    private static void ClearCheckDetails() => Preferences.Default.Remove(LastCheckedKey);

    private static void RemoveLegacyPreferences()
    {
        foreach (var key in LegacyPreferenceKeys)
            Preferences.Default.Remove(key);
    }

    private sealed record UserInfoResponse(
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("name")] string? Name);
}
