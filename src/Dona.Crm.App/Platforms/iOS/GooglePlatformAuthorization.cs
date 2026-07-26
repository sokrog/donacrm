namespace Dona.Crm.App.Services;

public sealed class GooglePlatformAuthorization(HttpClient http) : IGooglePlatformAuthorization
{
    private static readonly Uri CallbackUri = new("com.companyname.dona.crm.app:/oauth2redirect");

    public bool RequiresClientId => true;

    public async Task<GooglePlatformToken> AuthorizeAsync(
        string? clientId,
        bool interactive,
        string? refreshToken,
        CancellationToken cancellationToken = default)
    {
        clientId = RequireClientId(clientId);
        if (!interactive)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                throw new InvalidOperationException("Сессия Google завершена. Выполните вход ещё раз.");
            return await GoogleOAuthPkce.RefreshAsync(http, clientId, refreshToken, cancellationToken);
        }

        var verifier = GoogleOAuthPkce.CreateVerifier();
        var state = GoogleOAuthPkce.CreateState();
        WebAuthenticatorResult result;
        try
        {
            result = await WebAuthenticator.Default.AuthenticateAsync(new WebAuthenticatorOptions
            {
                Url = GoogleOAuthPkce.AuthorizationUri(clientId, CallbackUri, verifier, state),
                CallbackUrl = CallbackUri,
                PrefersEphemeralWebBrowserSession = false
            });
        }
        catch (TaskCanceledException)
        {
            throw new InvalidOperationException("Подключение Google отменено.");
        }

        if (result.Properties.TryGetValue("error", out var error) && !string.IsNullOrWhiteSpace(error))
            throw new InvalidOperationException($"Google OAuth: {error}.");
        if (!result.Properties.TryGetValue("state", out var returnedState) ||
            !string.Equals(returnedState, state, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Google OAuth вернул неверный state.");
        }
        if (!result.Properties.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Google OAuth не вернул код авторизации.");

        return await GoogleOAuthPkce.ExchangeAsync(
            http,
            clientId,
            CallbackUri,
            code,
            verifier,
            cancellationToken);
    }

    public async Task DisconnectAsync(string? accessToken, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(accessToken))
            await GoogleOAuthPkce.RevokeAsync(http, accessToken, cancellationToken);
    }

    private static string RequireClientId(string? clientId) =>
        !string.IsNullOrWhiteSpace(clientId) &&
        clientId.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase)
            ? clientId
            : throw new InvalidOperationException("Для iOS укажите OAuth Client ID типа iOS.");
}
