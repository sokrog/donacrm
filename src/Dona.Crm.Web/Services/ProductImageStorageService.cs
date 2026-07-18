using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using SkiaSharp;
using System.Net;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace Dona.Crm.Web.Services;

public sealed record ProductImageDownload(Stream Content, string ContentType);

public sealed class ProductImageStorageService(IWebHostEnvironment environment, IBusinessSettingsRepository businessSettings, GoogleDriveOAuthStore driveOAuth, IHttpClientFactory httpClientFactory)
{
    private static readonly IReadOnlyDictionary<string, string> Extensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["image/jpeg"] = ".jpg", ["image/png"] = ".png", ["image/webp"] = ".webp", ["image/gif"] = ".gif" };
    private readonly string _localRoot = Path.Combine(environment.WebRootPath, "uploads", "products");
    private const long MaxSourceSize = 25 * 1024 * 1024;
    private const long OptimizationThreshold = 1024 * 1024;
    private const int MaxDimension = 2048;

    public async Task<ProductImage> UploadAsync(Guid productId, Stream content, string fileName, string contentType, long sizeBytes, CancellationToken token = default)
    {
        if (!Extensions.ContainsKey(contentType)) throw new InvalidOperationException("Поддерживаются изображения JPG, PNG, WebP и GIF.");
        if (sizeBytes <= 0 || sizeBytes > MaxSourceSize) throw new InvalidOperationException("Размер исходного изображения должен быть от 1 байта до 25 МБ.");
        var optimized = await OptimizeAsync(content, fileName, contentType, sizeBytes, token);
        var settings = await businessSettings.GetAsync(token);
        await using var optimizedContent = optimized.Content;
        return settings.UseGoogleDriveImages
            ? await UploadDriveAsync(productId, optimizedContent, optimized.FileName, optimized.ContentType, optimized.Extension, optimized.SizeBytes, settings, token)
            : await UploadLocalAsync(productId, optimizedContent, optimized.FileName, optimized.ContentType, optimized.Extension, optimized.SizeBytes, token);
    }

    public async Task<ProductImage> UploadFromUrlAsync(Guid productId, string sourceUrl, CancellationToken token = default)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) throw new InvalidOperationException("Укажите полную ссылку HTTP или HTTPS.");
        using var response = await SendSafeAsync(uri, token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Источник вернул ошибку {(int)response.StatusCode}.");
        var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? string.Empty;
        if (!Extensions.ContainsKey(contentType)) throw new InvalidOperationException("По ссылке должен находиться JPG, PNG, WebP или GIF.");
        if (response.Content.Headers.ContentLength is > MaxSourceSize) throw new InvalidOperationException("Изображение по ссылке больше 25 МБ.");
        await using var source = await response.Content.ReadAsStreamAsync(token);
        await using var buffered = new MemoryStream();
        await CopyLimitedAsync(source, buffered, MaxSourceSize, token); buffered.Position = 0;
        var fileName = Path.GetFileName(Uri.UnescapeDataString(uri.AbsolutePath));
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "remote" + Extensions[contentType];
        return await UploadAsync(productId, buffered, fileName, contentType, buffered.Length, token);
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
            using var service = await driveOAuth.CreateDriveServiceAsync(token);
            var request = service.Files.Delete(image.StorageKey); request.SupportsAllDrives = true;
            await request.ExecuteAsync(token);
        }
    }

    public async Task<ProductImageDownload> DownloadDriveAsync(string fileId, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(fileId) || fileId.Any(x => !char.IsLetterOrDigit(x) && x is not '-' and not '_')) throw new InvalidOperationException("Некорректный ID файла.");
        using var service = await driveOAuth.CreateDriveServiceAsync(token);
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
        using var service = await driveOAuth.CreateDriveServiceAsync(token);
        if (string.IsNullOrWhiteSpace(settings.GoogleDriveFolderId))
        {
            var aboutRequest = service.About.Get(); aboutRequest.Fields = "user(emailAddress)"; await aboutRequest.ExecuteAsync(token); return;
        }
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
        using var service = await driveOAuth.CreateDriveServiceAsync(token);
        var safeName = $"{productId:N}_{Guid.NewGuid():N}{extension}";
        var metadata = new DriveFile { Name = safeName, Parents = string.IsNullOrWhiteSpace(settings.GoogleDriveFolderId) ? null : [settings.GoogleDriveFolderId], AppProperties = new Dictionary<string, string> { ["productId"] = productId.ToString(), ["originalName"] = Path.GetFileName(originalName) } };
        var request = service.Files.Create(metadata, content, contentType); request.Fields = "id,name,mimeType,size"; request.SupportsAllDrives = true;
        var result = await request.UploadAsync(token);
        if (result.Status != Google.Apis.Upload.UploadStatus.Completed || request.ResponseBody is null) throw new InvalidOperationException(result.Exception?.Message ?? "Google Drive не завершил загрузку файла.");
        var file = request.ResponseBody;
        return new ProductImage { FileName = Path.GetFileName(originalName), ContentType = file.MimeType ?? contentType, SizeBytes = file.Size ?? sizeBytes, Storage = ProductImageStorage.GoogleDrive, StorageKey = file.Id, Url = $"/media/drive/{file.Id}" };
    }

    private static async Task<OptimizedImage> OptimizeAsync(Stream source, string fileName, string contentType, long sizeBytes, CancellationToken token)
    {
        var original = new MemoryStream();
        await source.CopyToAsync(original, token); original.Position = 0;
        if (contentType.Equals("image/gif", StringComparison.OrdinalIgnoreCase)) return new OptimizedImage(original, Path.GetFileName(fileName), contentType, ".gif", original.Length);
        using var codec = SKCodec.Create(original) ?? throw new InvalidOperationException("Файл не удалось распознать как изображение.");
        if (codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 40_000_000) throw new InvalidOperationException("Разрешение изображения слишком большое.");
        var needsResize = codec.Info.Width > MaxDimension || codec.Info.Height > MaxDimension;
        if (!needsResize && sizeBytes <= OptimizationThreshold)
        {
            var untouched = new MemoryStream(original.ToArray()); original.Dispose();
            return new OptimizedImage(untouched, Path.GetFileName(fileName), contentType, Extensions[contentType], untouched.Length);
        }
        original.Position = 0;
        using var bitmap = SKBitmap.Decode(original) ?? throw new InvalidOperationException("Изображение повреждено или имеет неподдерживаемый формат.");
        var scale = Math.Min(1d, Math.Min(MaxDimension / (double)bitmap.Width, MaxDimension / (double)bitmap.Height));
        var targetInfo = new SKImageInfo(Math.Max(1, (int)Math.Round(bitmap.Width * scale)), Math.Max(1, (int)Math.Round(bitmap.Height * scale)), bitmap.ColorType, bitmap.AlphaType);
        using var resized = bitmap.Resize(targetInfo, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)) ?? throw new InvalidOperationException("Не удалось уменьшить изображение.");
        using var rendered = SKImage.FromBitmap(resized); using var encoded = rendered.Encode(SKEncodedImageFormat.Webp, 82);
        var output = new MemoryStream(); encoded.SaveTo(output); output.Position = 0;
        original.Dispose();
        return new OptimizedImage(output, Path.GetFileNameWithoutExtension(fileName) + ".webp", "image/webp", ".webp", output.Length);
    }

    private async Task<HttpResponseMessage> SendSafeAsync(Uri initialUri, CancellationToken token)
    {
        var client = httpClientFactory.CreateClient("product-images"); var current = initialUri;
        for (var redirect = 0; redirect <= 5; redirect++)
        {
            await EnsurePublicHostAsync(current, token);
            var response = await client.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is < 300 or >= 400) return response;
            var location = response.Headers.Location; response.Dispose();
            if (location is null) throw new InvalidOperationException("Источник вернул перенаправление без адреса.");
            current = location.IsAbsoluteUri ? location : new Uri(current, location);
            if (current.Scheme is not ("https" or "http")) throw new InvalidOperationException("Недопустимое перенаправление.");
        }
        throw new InvalidOperationException("Слишком много перенаправлений при загрузке изображения.");
    }

    private static async Task EnsurePublicHostAsync(Uri uri, CancellationToken token)
    {
        var addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, token);
        if (addresses.Length == 0 || addresses.Any(IsPrivateAddress)) throw new InvalidOperationException("Ссылки на локальные и внутренние адреса запрещены.");
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return true;
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            return bytes[0] == 10 || bytes[0] == 127 || bytes[0] == 0 || bytes[0] == 169 && bytes[1] == 254 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168;
        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || bytes[0] == 0xfc || bytes[0] == 0xfd;
    }

    private static async Task CopyLimitedAsync(Stream source, Stream destination, long limit, CancellationToken token)
    {
        var buffer = new byte[81920]; long total = 0;
        while (true) { var read = await source.ReadAsync(buffer, token); if (read == 0) break; total += read; if (total > limit) throw new InvalidOperationException("Изображение по ссылке больше 25 МБ."); await destination.WriteAsync(buffer.AsMemory(0, read), token); }
    }

    private string LocalFullPath(string relative)
    {
        var normalized = relative.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(environment.WebRootPath, normalized));
        var allowedRoot = Path.GetFullPath(_localRoot) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Некорректный путь изображения.");
        return fullPath;
    }

    private sealed record OptimizedImage(MemoryStream Content, string FileName, string ContentType, string Extension, long SizeBytes);
}
