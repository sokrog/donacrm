namespace Dona.Crm.Web.Services;

public sealed record GoogleConnectionSettings(
    string? OAuthClientId = null,
    string? OAuthClientSecret = null);

public sealed record GoogleConnectionState(
    GoogleConnectionSettings Settings,
    bool IsConnected,
    string? AccountEmail = null,
    string? AccountName = null,
    DateTimeOffset? LastCheckedAt = null)
{
    /// <summary>False when the platform still needs an OAuth client id before sign-in is possible.</summary>
    public bool IsConfigured { get; init; } = true;
}

public sealed record AppPlatformProfile(
    string StorageTitle,
    string StorageDescription,
    bool UsesDirectGoogleAccess,
    bool RequiresOAuthClientId,
    string OAuthClientDescription,
    bool SupportsOAuthClientSecret = false,
    string? OAuthClientSecretDescription = null);

public interface IGoogleConnectionService
{
    Task<GoogleConnectionState> GetStateAsync(CancellationToken cancellationToken = default);
    Task<GoogleConnectionState> SaveSettingsAsync(GoogleConnectionSettings settings, CancellationToken cancellationToken = default);
    Task<GoogleConnectionState> ConnectAsync(CancellationToken cancellationToken = default);
    Task<GoogleConnectionState> CheckAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}

public interface ISecureValueStore
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    bool Remove(string key);
}
