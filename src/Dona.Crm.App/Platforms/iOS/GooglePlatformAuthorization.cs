namespace Dona.Crm.App.Services;

public sealed class GooglePlatformAuthorization(HttpClient http) : IGooglePlatformAuthorization
{
    private const string ClientId =
        "440684132138-iudhahju73tqgkkjr4i7oetgnu8ra7mn.apps.googleusercontent.com";
    private static readonly Uri CallbackUri = new(
        "com.googleusercontent.apps.440684132138-iudhahju73tqgkkjr4i7oetgnu8ra7mn:/oauthredirect");

    public bool RequiresClientId => false;

    public async Task<GooglePlatformToken> AuthorizeAsync(
        string? clientId,
        string? clientSecret,
        bool interactive,
        string? refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (!interactive)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                throw new InvalidOperationException("Сессия Google завершена. Выполните вход ещё раз.");
            return await GoogleOAuthPkce.RefreshAsync(http, ClientId, null, refreshToken, cancellationToken);
        }

        var verifier = GoogleOAuthPkce.CreateVerifier();
        var state = GoogleOAuthPkce.CreateState();
        WebAuthenticatorResult result;
        try
        {
            result = await WebAuthenticator.Default.AuthenticateAsync(new WebAuthenticatorOptions
            {
                Url = GoogleOAuthPkce.AuthorizationUri(ClientId, CallbackUri, verifier, state),
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
            ClientId,
            null,
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

}
