using System.Security.Cryptography;
using System.Text.Json;
using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Services;

public sealed record ImageTransferProgress(int Completed, int Total);

public interface IImageTransferStore
{
    // Update only matching image references against current data, atomically.
    Task ApplyUploadedImageAsync(string localUrl, GoogleDriveFile file, CancellationToken cancellationToken = default);
}

public static class ImageTransferReferences
{
    public static void Apply(DonaSyncSnapshot snapshot, string localUrl, GoogleDriveFile file)
    {
        foreach (var image in LocalImageKey.EnumerateImages(snapshot)
                     .Where(x => x.Storage == ProductImageStorage.Local && x.Url == localUrl))
        {
            image.Storage = ProductImageStorage.GoogleDrive;
            image.StorageKey = file.Id;
            image.Url = $"drive:{file.Id}";
            image.ContentType = file.MimeType;
            image.SizeBytes = file.Size;
        }
        foreach (var product in snapshot.Products.Where(x => x.ImageUrl == localUrl))
            product.ImageUrl = $"drive:{file.Id}";
    }
}

/// <summary>Portable archives contain image bytes and never depend on the source Google account.</summary>
public sealed class PortableBackupService(
    IBackupSnapshotStore snapshots, ILocalImageStore localImages,
    GoogleDriveFileClient drive, IGoogleAccessTokenProvider tokens, HttpClient http)
{
    public const long MaxImageBytes = 10 * 1024 * 1024;
    public const long MaxTotalImageBytes = 200 * 1024 * 1024;

    public async Task<BackupDownload> ExportAsync(IProgress<ImageTransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        // A deep copy is essential: normal repositories may return mutable domain objects.
        var snapshot = JsonSerializer.Deserialize<DonaSyncSnapshot>(
            JsonSerializer.Serialize(await snapshots.ReadSnapshotAsync(cancellationToken)))!;
        IncludeLegacyProductImages(snapshot);
        var images = LocalImageKey.EnumerateImages(snapshot).ToList();
        var files = new Dictionary<string, BackupImage>(StringComparer.Ordinal);
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        var failures = new List<string>();
        long totalBytes = 0;
        progress?.Report(new(0, images.Count));
        for (var index = 0; index < images.Count; index++)
        {
            var image = images[index];
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var source = $"{image.Storage}:{(image.Storage == ProductImageStorage.GoogleDrive ? image.StorageKey : image.Url)}";
                if (!sources.TryGetValue(source, out var key))
                {
                    var bytes = await ReadImageAsync(image, cancellationToken);
                    var extension = Extension(bytes.ContentType);
                    if (bytes.Content.Length == 0 || bytes.Content.LongLength > MaxImageBytes)
                        throw new InvalidOperationException("Пустой файл или размер больше 10 МБ.");
                    key = $"portable-{Convert.ToHexString(SHA256.HashData(bytes.Content)).ToLowerInvariant()}{extension}";
                    if (!files.ContainsKey(key))
                    {
                        totalBytes += bytes.Content.LongLength;
                        if (totalBytes > MaxTotalImageBytes)
                            throw new InvalidOperationException("Общий размер фотографий превышает 200 МБ.");
                        files.Add(key, new(key, bytes.Content));
                    }
                    sources.Add(source, key);
                }
                image.Storage = ProductImageStorage.Local;
                image.StorageKey = key;
                image.Url = LocalImageKey.ToUrl(key);
                image.ContentType = LocalImageKey.ContentTypeFromKey(key);
                image.SizeBytes = files[key].Content.LongLength;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failures.Add($"Фото {index + 1} ({image.FileName}): {UserErrors.Describe(ex, "не удалось прочитать файл; проверьте доступ и подключение")}");
            }
            progress?.Report(new(index + 1, images.Count));
        }
        if (failures.Count > 0)
            throw new InvalidOperationException("Полная копия не создана. " + string.Join("\n", failures));
        foreach (var product in snapshot.Products)
            product.ImageUrl = product.PrimaryImage?.Url;
        var backup = BackupSnapshotMapper.FromSyncSnapshot(snapshot);
        backup.IncludesAllImages = true;
        var content = BackupArchiveCodec.Create(backup, files.Values);
        BackupArchiveCodec.Inspect(content);
        return new(content, $"dona-crm-full-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip");
    }

    public async Task<int> UploadLocalImagesAsync(IImageTransferStore target,
        IProgress<ImageTransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var snapshot = await snapshots.ReadSnapshotAsync(cancellationToken);
        var images = LocalImageKey.EnumerateImages(snapshot)
            .Where(x => x.Storage == ProductImageStorage.Local).DistinctBy(x => x.Url).ToList();
        progress?.Report(new(0, images.Count));
        var completed = 0;
        // Keep this operation bound to one account, even if a different view changes the connection.
        var accessToken = images.Count == 0 ? string.Empty : await tokens.GetAccessTokenAsync(cancellationToken);
        foreach (var image in images)
        {
            try
            {
                var bytes = await ReadImageAsync(image, cancellationToken);
                var extension = Extension(bytes.ContentType);
                if (bytes.Content.Length == 0 || bytes.Content.LongLength > MaxImageBytes)
                    throw new InvalidOperationException("Пустой файл или размер больше 10 МБ.");
                var file = await drive.UploadAppDataAsync($"dona-crm-image-{Guid.NewGuid():N}{extension}",
                    bytes.ContentType, bytes.Content, accessToken, cancellationToken);
                await target.ApplyUploadedImageAsync(image.Url, file, cancellationToken);
                completed++;
                progress?.Report(new(completed, images.Count));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException($"Перенесено фотографий: {completed} из {images.Count}. Не удалось перенести «{image.FileName}». Локальные файлы сохранены. Повторите перенос для оставшихся фотографий. " +
                    UserErrors.Describe(ex, "Проверьте подключение и доступ к Google."), ex);
            }
        }
        return completed;
    }

    private async Task<LocalImage> ReadImageAsync(ProductImage image, CancellationToken cancellationToken)
    {
        if (image.Storage == ProductImageStorage.GoogleDrive)
        {
            var result = await drive.DownloadAsync(image.StorageKey, await tokens.GetAccessTokenAsync(cancellationToken), cancellationToken);
            return new(result.Content, result.ContentType);
        }
        if (LocalImageKey.TryParseUrl(image.Url, out var key))
            return await localImages.ReadAsync(key, cancellationToken)
                ?? throw new InvalidOperationException("Локальная фотография отсутствует на этом устройстве.");
        if (LocalImageKey.TryParseDataUrl(image.Url, out var type, out var content))
            return new(content, type);
        if (!Uri.TryCreate(image.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Некорректная ссылка фотографии.");
        // No Google credentials are sent to external image URLs.
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxImageBytes)
            throw new InvalidOperationException("Фотография превышает 10 МБ.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > MaxImageBytes) throw new InvalidOperationException("Фотография превышает 10 МБ.");
            output.Write(buffer, 0, read);
        }
        return new(output.ToArray(), response.Content.Headers.ContentType?.MediaType ?? image.ContentType);
    }

    private static string Extension(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/jpeg" or "image/jpg" => ".jpg", "image/png" => ".png", "image/webp" => ".webp",
        "image/gif" => ".gif", "image/heic" => ".heic", "image/heif" => ".heif",
        _ => throw new InvalidOperationException("Неподдерживаемый формат фотографии.")
    };

    private static void IncludeLegacyProductImages(DonaSyncSnapshot snapshot)
    {
        foreach (var product in snapshot.Products.Where(x => x.Images.Count == 0 && !string.IsNullOrWhiteSpace(x.ImageUrl)))
        {
            var url = product.ImageUrl!;
            product.Images.Add(new ProductImage
            {
                Url = url, FileName = product.Name, IsMain = true,
                Storage = url.StartsWith("drive:", StringComparison.Ordinal) ? ProductImageStorage.GoogleDrive : ProductImageStorage.Local,
                StorageKey = url.StartsWith("drive:", StringComparison.Ordinal) ? url[6..] : string.Empty
            });
        }
    }
}
