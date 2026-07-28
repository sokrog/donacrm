using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Dona.Crm.Web.Services;

public sealed record GoogleDriveFile(string Id, string Name, string MimeType, long Size, DateTimeOffset? ModifiedAt = null);
public sealed record GoogleDriveDownload(byte[] Content, string ContentType);

public sealed class GoogleDriveFileClient(HttpClient http)
{
    private const long MaxDownloadBytes = 10 * 1024 * 1024;
    private const long MaxBackupDownloadBytes = 50 * 1024 * 1024;

    public async Task<GoogleDriveFile> UploadAsync(
        string? folderId,
        string fileName,
        string contentType,
        byte[] content,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var metadata = JsonSerializer.Serialize(new
        {
            name = Path.GetFileName(fileName),
            parents = string.IsNullOrWhiteSpace(folderId) ? null : new[] { folderId }
        });
        var boundary = $"dona_{Guid.NewGuid():N}";
        using var multipart = new MultipartContent("related", boundary);
        using var metadataContent = new StringContent(metadata, Encoding.UTF8, "application/json");
        using var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        multipart.Add(metadataContent);
        multipart.Add(fileContent);

        using var response = await SendAsync(
            HttpMethod.Post,
            "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id,name,mimeType,size&supportsAllDrives=true",
            accessToken,
            multipart,
            cancellationToken);
        await EnsureSuccessAsync(response, "Не удалось загрузить фотографию в Google Drive", cancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return ParseFile(document.RootElement);
    }

    /// <summary>Stores a private app archive in the signed-in user's hidden Drive appData folder.</summary>
    public Task<GoogleDriveFile> UploadAppDataAsync(
        string fileName,
        string contentType,
        byte[] content,
        string accessToken,
        CancellationToken cancellationToken = default) =>
        UploadAsync("appDataFolder", fileName, contentType, content, accessToken, cancellationToken);

    public async Task<IReadOnlyList<GoogleDriveFile>> ListAppDataAsync(
        string namePrefix,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var escapedPrefix = namePrefix.Replace("'", "\\'");
        var query = $"name contains '{escapedPrefix}'";
        var url = "https://www.googleapis.com/drive/v3/files?spaces=appDataFolder" +
                  $"&q={Uri.EscapeDataString(query)}" +
                  "&orderBy=modifiedTime%20desc&fields=files(id,name,mimeType,size,modifiedTime)";
        using var response = await SendAsync(HttpMethod.Get, url, accessToken, null, cancellationToken);
        await EnsureSuccessAsync(response, "Не удалось получить список личных копий Google Drive", cancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.TryGetProperty("files", out var files)
            ? files.EnumerateArray().Select(ParseFile).ToList()
            : [];
    }

    public async Task<GoogleDriveFile> GetMetadataAsync(
        string fileId,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ValidateId(fileId);
        using var response = await SendAsync(
            HttpMethod.Get,
            $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(fileId)}?fields=id,name,mimeType,size&supportsAllDrives=true",
            accessToken,
            null,
            cancellationToken);
        await EnsureSuccessAsync(response, "Не удалось открыть объект Google Drive", cancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return ParseFile(document.RootElement);
    }

    public async Task<GoogleDriveDownload> DownloadAsync(
        string fileId,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ValidateId(fileId);
        using var response = await SendAsync(
            HttpMethod.Get,
            $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(fileId)}?alt=media&supportsAllDrives=true",
            accessToken,
            null,
            cancellationToken);
        await EnsureSuccessAsync(response, "Не удалось скачать фотографию из Google Drive", cancellationToken);
        return await ReadDownloadAsync(response, MaxDownloadBytes, "Фотография в Google Drive превышает 10 МБ.", cancellationToken);
    }

    public async Task<GoogleDriveDownload> DownloadAppDataAsync(
        string fileId,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ValidateId(fileId);
        using var response = await SendAsync(
            HttpMethod.Get,
            $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(fileId)}?alt=media",
            accessToken,
            null,
            cancellationToken);
        await EnsureSuccessAsync(response, "Не удалось скачать личную копию Google Drive", cancellationToken);
        return await ReadDownloadAsync(response, MaxBackupDownloadBytes, "Личная копия Google Drive превышает 50 МБ.", cancellationToken);
    }

    private static async Task<GoogleDriveDownload> ReadDownloadAsync(
        HttpResponseMessage response,
        long maxBytes,
        string tooLargeMessage,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long length && length > maxBytes)
            throw new InvalidDataException(tooLargeMessage);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;
            if (output.Length + read > maxBytes)
                throw new InvalidDataException(tooLargeMessage);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return new(
            output.ToArray(),
            response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
    }

    public async Task DeleteAsync(
        string fileId,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ValidateId(fileId);
        using var response = await SendAsync(
            HttpMethod.Delete,
            $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(fileId)}?supportsAllDrives=true",
            accessToken,
            null,
            cancellationToken);
        await EnsureSuccessAsync(response, "Не удалось удалить фотографию из Google Drive", cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string url,
        string accessToken,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = content;
        return await http.SendAsync(request, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string message,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"{message}: {detail}");
    }

    private static GoogleDriveFile ParseFile(JsonElement value) => new(
        value.GetProperty("id").GetString() ?? throw new InvalidOperationException("Google Drive не вернул ID файла."),
        value.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
        value.TryGetProperty("mimeType", out var mimeType) ? mimeType.GetString() ?? string.Empty : string.Empty,
        value.TryGetProperty("size", out var size) && long.TryParse(size.ToString(), out var parsedSize) ? parsedSize : 0,
        value.TryGetProperty("modifiedTime", out var modifiedTime) && DateTimeOffset.TryParse(modifiedTime.GetString(), out var parsedModifiedAt)
            ? parsedModifiedAt
            : null);

    private static void ValidateId(string fileId)
    {
        if (string.IsNullOrWhiteSpace(fileId) ||
            fileId.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("Некорректный ID файла Google Drive.", nameof(fileId));
        }
    }
}
