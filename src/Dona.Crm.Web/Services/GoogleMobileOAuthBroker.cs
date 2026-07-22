using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Dona.Crm.Web.Services;

public sealed class GoogleMobileOAuthOptions
{
    public const string SectionName = "GoogleMobileOAuth";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string PublicBaseUrl { get; set; } = string.Empty;
}

public sealed record GoogleMobileToken(string AccessToken, string RefreshToken, int ExpiresIn, string Email);
public sealed record GoogleGrantRequest(string Grant);
public sealed record GoogleRefreshRequest(string RefreshToken);

public sealed class GoogleMobileOAuthGrantStore
{
    private readonly ConcurrentDictionary<string, PendingAuthorization> _pending = [];
    private readonly ConcurrentDictionary<string, TemporaryGrant> _grants = [];

    public string AddPending(Uri callback, string verifier)
    {
        Cleanup();
        var state = Secret();
        _pending[state] = new PendingAuthorization(callback, verifier, DateTimeOffset.UtcNow.AddMinutes(10));
        return state;
    }

    public bool TryTakePending(string state, out PendingAuthorization pending)
    {
        Cleanup();
        return _pending.TryRemove(state, out pending!);
    }

    public string AddGrant(GoogleMobileToken token)
    {
        Cleanup();
        var grant = Secret();
        _grants[grant] = new TemporaryGrant(token, DateTimeOffset.UtcNow.AddMinutes(2));
        return grant;
    }

    public bool TryTakeGrant(string grant, out GoogleMobileToken token)
    {
        Cleanup();
        if (_grants.TryRemove(grant, out var value) && value.ExpiresAt > DateTimeOffset.UtcNow)
        {
            token = value.Token;
            return true;
        }
        token = null!;
        return false;
    }

    private void Cleanup()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var item in _pending.Where(x => x.Value.ExpiresAt <= now)) _pending.TryRemove(item.Key, out _);
        foreach (var item in _grants.Where(x => x.Value.ExpiresAt <= now)) _grants.TryRemove(item.Key, out _);
    }

    private static string Secret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public sealed record PendingAuthorization(Uri Callback, string Verifier, DateTimeOffset ExpiresAt);
    private sealed record TemporaryGrant(GoogleMobileToken Token, DateTimeOffset ExpiresAt);
}

public sealed class GoogleMobileOAuthBroker(
    IOptions<GoogleMobileOAuthOptions> configuration,
    IHttpClientFactory clients,
    GoogleMobileOAuthGrantStore grants)
{
    private readonly GoogleMobileOAuthOptions _options = configuration.Value;
    private static readonly Uri ExpectedAppCallback = new("donacrm://oauth2redirect");
    private const string Scopes = "openid email profile https://www.googleapis.com/auth/spreadsheets https://www.googleapis.com/auth/drive.file";

    public Uri Start(string callback)
    {
        EnsureConfigured();
        if (!Uri.TryCreate(callback, UriKind.Absolute, out var callbackUri) || callbackUri != ExpectedAppCallback)
            throw new InvalidOperationException("Недопустимый callback мобильного приложения.");

        var verifier = Secret(64);
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = grants.AddPending(callbackUri, verifier);
        return new Uri("https://accounts.google.com/o/oauth2/v2/auth?" + Query(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = CallbackUri.ToString(),
            ["response_type"] = "code",
            ["scope"] = Scopes,
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256"
        }));
    }

    public async Task<Uri> CompleteAsync(string code, string state, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (!grants.TryTakePending(state, out var pending)) throw new InvalidOperationException("OAuth-сессия истекла или уже использована.");
        using var response = await clients.CreateClient("google-mobile-oauth").PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["code"] = code,
                ["code_verifier"] = pending.Verifier,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = CallbackUri.ToString()
            }), cancellationToken);
        var token = await ReadTokenAsync(response, cancellationToken);
        if (string.IsNullOrWhiteSpace(token.RefreshToken)) throw new InvalidOperationException("Google не вернул refresh token.");
        var email = await GetEmailAsync(token.AccessToken, cancellationToken);
        var grant = grants.AddGrant(new GoogleMobileToken(token.AccessToken, token.RefreshToken, token.ExpiresIn, email));
        return new UriBuilder(pending.Callback) { Query = $"grant={Uri.EscapeDataString(grant)}" }.Uri;
    }

    public GoogleMobileToken Exchange(string grant)
    {
        if (!grants.TryTakeGrant(grant, out var token)) throw new InvalidOperationException("Одноразовый код истёк или уже использован.");
        return token;
    }

    public async Task<GoogleMobileToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(refreshToken)) throw new InvalidOperationException("Refresh token не указан.");
        using var response = await clients.CreateClient("google-mobile-oauth").PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token"
            }), cancellationToken);
        var token = await ReadTokenAsync(response, cancellationToken);
        var email = await GetEmailAsync(token.AccessToken, cancellationToken);
        return new GoogleMobileToken(token.AccessToken, refreshToken, token.ExpiresIn, email);
    }

    private async Task<string> GetEmailAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await clients.CreateClient("google-mobile-oauth").SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Не удалось определить Google-аккаунт.");
        var profile = await response.Content.ReadFromJsonAsync<UserProfile>(cancellationToken: cancellationToken);
        return profile?.Email ?? "Google Account";
    }

    private static async Task<RawToken> ReadTokenAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var token = await response.Content.ReadFromJsonAsync<RawToken>(cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode || token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            throw new InvalidOperationException(token?.ErrorDescription ?? "Google отклонил запрос токена.");
        return token;
    }

    private Uri CallbackUri => new($"{GoogleResourceIds.BrokerBaseUrl(_options.PublicBaseUrl)}/api/mobile/google/callback");

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret)
            || string.IsNullOrWhiteSpace(GoogleResourceIds.BrokerBaseUrl(_options.PublicBaseUrl)))
            throw new InvalidOperationException("Google Mobile OAuth не настроен на сервере DONA CRM.");
    }

    private static string Query(IReadOnlyDictionary<string, string> values) => string.Join("&", values.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
    private static string Secret(int bytes) => Base64Url(RandomNumberGenerator.GetBytes(bytes));
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record RawToken(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("error_description")] string? ErrorDescription);
    private sealed record UserProfile([property: JsonPropertyName("email")] string? Email);
}
