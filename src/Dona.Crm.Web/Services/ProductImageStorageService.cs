using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace Dona.Crm.Web.Services;

public sealed record ProductImageDownload(Stream Content, string ContentType);

public sealed class ProductImageStorageService(IWebHostEnvironment environment, IBusinessSettingsRepository businessSettings, GoogleSheetsSettingsStore googleSettings)
{
    private static readonly IReadOnlyDictionary<string, string> Extensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["image/jpeg"] = ".jpg", ["image/png"] = ".png", ["image/webp"] = ".webp", ["image/gif"] = ".gif" };
    private readonly string _localRoot = Path.Combine(environment.WebRootPath, "uploads", "products");

    public async Task<ProductImage> UploadAsync(Guid productId, Stream content, string fileName, string contentType, long sizeBytes, CancellationToken token = default)
    {
        if (!Extensions.TryGetValue(contentType, out var extension)) throw new InvalidOperationException("Поддерживаются изображения JPG, PNG, WebP и GIF.");
        if (sizeBytes <= 0 || sizeBytes > 10 * 1024 * 1024) throw new InvalidOperationException("Размер изображения должен быть от 1 байта до 10 МБ.");
        var settings = await businessSettings.GetAsync(token);
        return settings.UseGoogleDriveImages ? await UploadDriveAsync(productId, content, fileName, contentType, extension, sizeBytes, settings, token) : await UploadLocalAsync(productId, content, fileName, contentType, extension, sizeBytes, token);
    }

    public async Task DeleteAsync(ProductImage image, CancellationToken token = default)
    {
        if (image.Storage == ProductImageStorage.Local)
        {
            var fullPath = LocalFullPath(image.StorageKey);
            if (File.Exists(fullPath)) File.Delete(fullPath);
            return;
        }
        if (image.Storage == ProductImageStorage.GoogleDrive && !string.IsNullOrWhiteSpace(image.StorageKey))
        {
            using var service = CreateDriveService();
            var request = service.Files.Delete(image.StorageKey); request.SupportsAllDrives = true;
            await request.ExecuteAsync(token);
        }
    }

    public async Task<ProductImageDownload> DownloadDriveAsync(string fileId, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(fileId) || fileId.Any(x => !char.IsLetterOrDigit(x) && x is not '-' and not '_')) throw new InvalidOperationException("Некорректный ID файла.");
        using var service = CreateDriveService();
        var metadataRequest = service.Files.Get(fileId); metadataRequest.Fields = "id,mimeType"; metadataRequest.SupportsAllDrives = true;
        var metadata = await metadataRequest.ExecuteAsync(token);
        var stream = new MemoryStream();
        var request = service.Files.Get(fileId); request.SupportsAllDrives = true;
        await request.DownloadAsync(stream, token); stream.Position = 0;
        return new ProductImageDownload(stream, metadata.MimeType ?? "application/octet-stream");
    }

    public async Task CheckGoogleDriveAsync(CancellationToken token = default)
    {
        var settings = await businessSettings.GetAsync(token);
        if (string.IsNullOrWhiteSpace(settings.GoogleDriveFolderId)) throw new InvalidOperationException("Укажите ID папки Google Drive.");
        using var service = CreateDriveService();
        var request = service.Files.Get(settings.GoogleDriveFolderId); request.Fields = "id,name,mimeType"; request.SupportsAllDrives = true;
        var folder = await request.ExecuteAsync(token);
        if (folder.MimeType != "application/vnd.google-apps.folder") throw new InvalidOperationException("Указанный ID не является папкой Google Drive.");
    }

    private async Task<ProductImage> UploadLocalAsync(Guid productId, Stream content, string originalName, string contentType, string extension, long sizeBytes, CancellationToken token)
    {
        var id = Guid.NewGuid(); var relative = Path.Combine("uploads", "products", productId.ToString("N"), id.ToString("N") + extension);
        var fullPath = LocalFullPath(relative); Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using (var output = File.Create(fullPath)) await content.CopyToAsync(output, token);
        return new ProductImage { Id = id, FileName = Path.GetFileName(originalName), ContentType = contentType, SizeBytes = sizeBytes, Storage = ProductImageStorage.Local, StorageKey = relative.Replace('\\', '/'), Url = "/" + relative.Replace('\\', '/') };
    }

    private async Task<ProductImage> UploadDriveAsync(Guid productId, Stream content, string originalName, string contentType, string extension, long sizeBytes, BusinessSettings settings, CancellationToken token)
    {
        if (!googleSettings.HasCredentials) throw new InvalidOperationException("Сначала сохраните JSON-ключ сервисного аккаунта.");
        if (string.IsNullOrWhiteSpace(settings.GoogleDriveFolderId)) throw new InvalidOperationException("Укажите папку Google Drive в настройках.");
        using var service = CreateDriveService();
        var safeName = $"{productId:N}_{Guid.NewGuid():N}{extension}";
        var metadata = new DriveFile { Name = safeName, Parents = [settings.GoogleDriveFolderId], AppProperties = new Dictionary<string, string> { ["productId"] = productId.ToString(), ["originalName"] = Path.GetFileName(originalName) } };
        var request = service.Files.Create(metadata, content, contentType); request.Fields = "id,name,mimeType,size"; request.SupportsAllDrives = true;
        var result = await request.UploadAsync(token);
        if (result.Status != Google.Apis.Upload.UploadStatus.Completed || request.ResponseBody is null) throw new InvalidOperationException(result.Exception?.Message ?? "Google Drive не завершил загрузку файла.");
        var file = request.ResponseBody;
        return new ProductImage { FileName = Path.GetFileName(originalName), ContentType = file.MimeType ?? contentType, SizeBytes = file.Size ?? sizeBytes, Storage = ProductImageStorage.GoogleDrive, StorageKey = file.Id, Url = $"/media/drive/{file.Id}" };
    }

    private DriveService CreateDriveService()
    {
        if (!googleSettings.HasCredentials) throw new InvalidOperationException("JSON-ключ сервисного аккаунта не найден.");
        var credential = CredentialFactory.FromFile<ServiceAccountCredential>(googleSettings.CredentialsFullPath).ToGoogleCredential().CreateScoped(DriveService.Scope.Drive);
        return new DriveService(new BaseClientService.Initializer { HttpClientInitializer = credential, ApplicationName = "Dona CRM" });
    }

    private string LocalFullPath(string relative)
    {
        var normalized = relative.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(environment.WebRootPath, normalized));
        var allowedRoot = Path.GetFullPath(_localRoot) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Некорректный путь изображения.");
        return fullPath;
    }
}
