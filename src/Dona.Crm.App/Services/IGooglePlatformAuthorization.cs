namespace Dona.Crm.App.Services;

public sealed record GooglePlatformToken(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string? RefreshToken = null);

public interface IGooglePlatformAuthorization
{
    bool RequiresClientId { get; }

    Task<GooglePlatformToken> AuthorizeAsync(
        string? clientId,
        string? clientSecret,
        bool interactive,
        string? refreshToken,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync(string? accessToken, CancellationToken cancellationToken = default);
}
