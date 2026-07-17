using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Dona.Crm.Web.Storage;

public sealed partial class GoogleSheetsSettingsStore
{
    private readonly string _settingsPath;
    private readonly string _credentialsPath;
    private string _spreadsheetId;

    public GoogleSheetsSettingsStore(IWebHostEnvironment environment, IOptions<GoogleSheetsOptions> defaults)
    {
        _settingsPath = Path.Combine(environment.ContentRootPath, "data", "google-sheets-settings.json");
        _credentialsPath = Path.GetFullPath(defaults.Value.CredentialsPath, environment.ContentRootPath);
        _spreadsheetId = LoadSpreadsheetId() ?? defaults.Value.SpreadsheetId;
    }

    public string SpreadsheetId => _spreadsheetId;
    public string CredentialsFullPath => _credentialsPath;
    public bool HasCredentials => File.Exists(_credentialsPath);
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_spreadsheetId) && HasCredentials;
    public int Version { get; private set; }
    public string? ServiceAccountEmail => ReadCredentialField("client_email");

    public async Task SaveAsync(string spreadsheetIdOrUrl, string? credentialsJson, CancellationToken cancellationToken = default)
    {
        var spreadsheetId = ExtractSpreadsheetId(spreadsheetIdOrUrl);
        if (string.IsNullOrWhiteSpace(spreadsheetId))
            throw new InvalidOperationException("Укажите корректный ID или ссылку на Google-таблицу.");

        if (!string.IsNullOrWhiteSpace(credentialsJson)) ValidateCredentials(credentialsJson);
        else if (!HasCredentials) throw new InvalidOperationException("Загрузите JSON-ключ сервисного аккаунта или вставьте его содержимое.");

        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var settingsJson = JsonSerializer.Serialize(new PersistedSettings(spreadsheetId), new JsonSerializerOptions { WriteIndented = true });
        await WriteAtomicallyAsync(_settingsPath, settingsJson, cancellationToken);

        if (!string.IsNullOrWhiteSpace(credentialsJson))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_credentialsPath)!);
            await WriteAtomicallyAsync(_credentialsPath, credentialsJson, cancellationToken);
        }

        _spreadsheetId = spreadsheetId;
        Version++;
    }

    private string? LoadSpreadsheetId()
    {
        if (!File.Exists(_settingsPath)) return null;
        try { return JsonSerializer.Deserialize<PersistedSettings>(File.ReadAllText(_settingsPath))?.SpreadsheetId; }
        catch (JsonException) { return null; }
    }

    private string? ReadCredentialField(string name)
    {
        if (!HasCredentials) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_credentialsPath));
            return document.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static void ValidateCredentials(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeNode) ? typeNode.GetString() : null;
            var email = root.TryGetProperty("client_email", out var emailNode) ? emailNode.GetString() : null;
            var key = root.TryGetProperty("private_key", out var keyNode) ? keyNode.GetString() : null;
            if (type != "service_account" || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException("JSON не похож на ключ сервисного аккаунта: нужны type, client_email и private_key.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("JSON-ключ имеет некорректный формат.", exception);
        }
    }

    private static string ExtractSpreadsheetId(string value)
    {
        value = value.Trim();
        var match = SpreadsheetUrlRegex().Match(value);
        if (match.Success) return match.Groups[1].Value;
        return SpreadsheetIdRegex().IsMatch(value) ? value : string.Empty;
    }

    private static async Task WriteAtomicallyAsync(string path, string content, CancellationToken cancellationToken)
    {
        var temporaryPath = path + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, content, cancellationToken);
        File.Move(temporaryPath, path, true);
    }

    private sealed record PersistedSettings(string SpreadsheetId);
    [GeneratedRegex(@"/spreadsheets/d/([a-zA-Z0-9_-]+)", RegexOptions.IgnoreCase)] private static partial Regex SpreadsheetUrlRegex();
    [GeneratedRegex(@"^[a-zA-Z0-9_-]{10,}$")] private static partial Regex SpreadsheetIdRegex();
}
