using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dona.Crm.Web.Services;

namespace Dona.Crm.App.Services;

internal static class GoogleOAuthPkce
{
    internal const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    internal const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    internal const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";
    internal const string Scopes = "openid email profile https://www.googleapis.com/auth/drive.appdata https://www.googleapis.com/auth/drive.file";
    private static readonly string[] RequiredDataScopes =
    [
        "https://www.googleapis.com/auth/drive.appdata",
        "https://www.googleapis.com/auth/drive.file"
    ];

    public static string CreateVerifier() => Base64Url(RandomNumberGenerator.GetBytes(64));

    public static string CreateChallenge(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static string CreateState() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static Uri AuthorizationUri(
        string clientId,
        Uri redirectUri,
        string verifier,
        string state)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri.ToString(),
            ["response_type"] = "code",
            ["scope"] = Scopes,
            ["code_challenge"] = CreateChallenge(verifier),
            ["code_challenge_method"] = "S256",
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["state"] = state
        };
        return new Uri($"{AuthorizationEndpoint}?{string.Join("&", query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"))}");
    }

    public static async Task<GooglePlatformToken> ExchangeAsync(
        HttpClient http,
        string clientId,
        string? clientSecret,
        Uri redirectUri,
        string code,
        string verifier,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri.ToString(),
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["grant_type"] = "authorization_code"
        };
        AddClientSecret(parameters, clientSecret);
        using var response = await http.PostAsync(
            TokenEndpoint,
            new FormUrlEncodedContent(parameters),
            cancellationToken);
        return await ReadTokenAsync(response, cancellationToken);
    }

    public static async Task<GooglePlatformToken> RefreshAsync(
        HttpClient http,
        string clientId,
        string? clientSecret,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        };
        AddClientSecret(parameters, clientSecret);
        using var response = await http.PostAsync(
            TokenEndpoint,
            new FormUrlEncodedContent(parameters),
            cancellationToken);
        var token = await ReadTokenAsync(response, cancellationToken);
        return token with { RefreshToken = refreshToken };
    }

    public static async Task RevokeAsync(
        HttpClient http,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var response = await http.PostAsync(
            RevokeEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = accessToken }),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Google не подтвердил отзыв доступа.");
    }

    private static async Task<GooglePlatformToken> ReadTokenAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (content.Contains("client_secret is missing", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Этот OAuth-клиент Google требует Client Secret. Вставьте его из JSON OAuth-клиента в настройках подключения и повторите вход.");
            throw new GoogleApiException(
                GoogleErrorMessages.Describe(response.StatusCode, content, "Google OAuth вернул ошибку"),
                response.StatusCode);
        }

        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        var accessToken = root.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Google OAuth не вернул access token.");
        var expiresIn = root.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 3600;
        var refreshToken = root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null;
        if (root.TryGetProperty("scope", out var scopeElement))
            EnsureRequiredScopes(scopeElement.GetString());
        return new(accessToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn), refreshToken);
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void AddClientSecret(IDictionary<string, string> parameters, string? clientSecret)
    {
        if (!string.IsNullOrWhiteSpace(clientSecret))
            parameters["client_secret"] = clientSecret.Trim();
    }

    private static void EnsureRequiredScopes(string? grantedScopes)
    {
        var granted = (grantedScopes ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
        var missing = RequiredDataScopes.Where(scope => !granted.Contains(scope)).ToArray();
        if (missing.Length == 0)
            return;

        throw new InvalidOperationException(
            "Google не выдал обязательные разрешения Google Drive (данные приложения и файлы приложения). Повторите вход и отметьте все запрошенные разрешения на экране Google.");
    }
}
