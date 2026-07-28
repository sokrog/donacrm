using System.Text.RegularExpressions;

namespace Dona.Crm.Web.Services;

public sealed record GoogleConnectionSettings(
    string SpreadsheetId,
    string? DriveFolderId,
    string? OAuthClientId = null,
    string? OAuthClientSecret = null);

public sealed record GoogleConnectionState(
    GoogleConnectionSettings Settings,
    bool IsConnected,
    string? AccountEmail = null,
    string? AccountName = null,
    string? SpreadsheetName = null,
    string? DriveFolderName = null,
    DateTimeOffset? LastCheckedAt = null)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Settings.SpreadsheetId);
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

public static partial class GoogleResourceIds
{
    public static string Spreadsheet(string? value)
    {
        value = value?.Trim() ?? string.Empty;
        var match = SpreadsheetUrl().Match(value);
        if (match.Success) return match.Groups[1].Value;
        return ResourceId().IsMatch(value) ? value : string.Empty;
    }

    public static string? DriveFolder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        var match = DriveFolderUrl().Match(value);
        if (match.Success) return match.Groups[1].Value;
        return ResourceId().IsMatch(value) ? value : string.Empty;
    }

    [GeneratedRegex(@"/spreadsheets/d/([a-zA-Z0-9_-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex SpreadsheetUrl();

    [GeneratedRegex(@"/folders/([a-zA-Z0-9_-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex DriveFolderUrl();

    [GeneratedRegex(@"^[a-zA-Z0-9_-]{10,}$")]
    private static partial Regex ResourceId();
}
