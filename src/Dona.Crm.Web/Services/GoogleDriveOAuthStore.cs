using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System.Text.Json;

namespace Dona.Crm.Web.Services;

public sealed class GoogleDriveOAuthStore
{
    private const string UserId = "dona-crm-personal-drive";
    private readonly string _clientPath;
    private readonly string _tokenDirectory;

    public GoogleDriveOAuthStore(IWebHostEnvironment environment)
    {
        var credentialsRoot = Path.Combine(environment.ContentRootPath, "credentials");
        _clientPath = Path.Combine(credentialsRoot, "google-drive-oauth-client.json");
        _tokenDirectory = Path.Combine(credentialsRoot, "google-drive-token");
    }

    public bool HasClientCredentials => File.Exists(_clientPath);
    public bool IsConnected => HasClientCredentials && Directory.Exists(_tokenDirectory) && Directory.EnumerateFiles(_tokenDirectory, "*.json").Any();

    public async Task SaveClientCredentialsAsync(string json, CancellationToken token = default)
    {
        ValidateClientCredentials(json);
        Directory.CreateDirectory(Path.GetDirectoryName(_clientPath)!);
        var temporary = _clientPath + ".tmp";
        await File.WriteAllTextAsync(temporary, json, token);
        File.Move(temporary, _clientPath, true);
    }

    public async Task<string> ConnectAsync(CancellationToken token = default)
    {
        var credential = await GetCredentialAsync(requireExistingToken: false, token);
        using var drive = new DriveService(new BaseClientService.Initializer { HttpClientInitializer = credential, ApplicationName = "Dona CRM" });
        var request = drive.About.Get(); request.Fields = "user(displayName,emailAddress)";
        var about = await request.ExecuteAsync(token);
        return about.User?.EmailAddress ?? about.User?.DisplayName ?? "Google Drive";
    }

    public async Task<DriveService> CreateDriveServiceAsync(CancellationToken token = default)
    {
        var credential = await GetCredentialAsync(requireExistingToken: true, token);
        return new DriveService(new BaseClientService.Initializer { HttpClientInitializer = credential, ApplicationName = "Dona CRM" });
    }

    public void Disconnect()
    {
        if (Directory.Exists(_tokenDirectory)) Directory.Delete(_tokenDirectory, recursive: true);
    }

    private async Task<UserCredential> GetCredentialAsync(bool requireExistingToken, CancellationToken token)
    {
        if (!HasClientCredentials) throw new InvalidOperationException("Загрузите OAuth JSON типа «Desktop app».");
        if (requireExistingToken && !IsConnected) throw new InvalidOperationException("Сначала подключите личный Google Drive в настройках.");
        await using var stream = File.OpenRead(_clientPath);
        var secrets = (await GoogleClientSecrets.FromStreamAsync(stream, token)).Secrets;
        return await GoogleWebAuthorizationBroker.AuthorizeAsync(secrets, [DriveService.Scope.Drive], UserId, token, new FileDataStore(_tokenDirectory, true));
    }

    private static void ValidateClientCredentials(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("installed", out var installed)) throw new InvalidOperationException("Нужен OAuth-клиент типа Desktop app, а не ключ сервисного аккаунта.");
            var clientId = installed.TryGetProperty("client_id", out var id) ? id.GetString() : null;
            var clientSecret = installed.TryGetProperty("client_secret", out var secret) ? secret.GetString() : null;
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret)) throw new InvalidOperationException("В OAuth JSON отсутствуют client_id или client_secret.");
        }
        catch (JsonException exception) { throw new InvalidOperationException("OAuth JSON имеет некорректный формат.", exception); }
    }
}
