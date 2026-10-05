using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.App.Services;

public sealed class MauiProductImagePicker(
    IGoogleConnectionService google,
    IGoogleAccessTokenProvider tokens,
    GoogleDriveFileClient drive,
    ILocalImageStore localImages) : IProductImagePicker
{
    private const long MaxImageBytes = 5 * 1024 * 1024;

    public async Task<ProductImage?> PickAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var results = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions
        {
            SelectionLimit = 1,
            MaximumWidth = 1600,
            MaximumHeight = 1600,
            CompressionQuality = 82,
            PreserveMetaData = false,
            RotateImage = true,
            Title = "Выберите фотографию"
        });
        var result = results.FirstOrDefault();
        if (result is null) return null;

        await using var source = await result.OpenReadAsync();
        await using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > MaxImageBytes)
                throw new InvalidDataException("Фотография должна быть меньше 5 МБ.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        var contentType = NormalizeContentType(result.ContentType, result.FileName);
        var extension = ExtensionFor(contentType);
        var bytes = buffer.ToArray();
        var state = await google.GetStateAsync(cancellationToken);
        if (state.IsConnected)
        {
            var uploaded = await drive.UploadAppDataAsync(
                $"dona-crm-image-{productId:N}-{Guid.NewGuid():N}{extension}",
                contentType,
                bytes,
                await tokens.GetAccessTokenAsync(cancellationToken),
                cancellationToken);
            return new ProductImage
            {
                FileName = result.FileName,
                ContentType = uploaded.MimeType,
                SizeBytes = uploaded.Size,
                Storage = ProductImageStorage.GoogleDrive,
                StorageKey = uploaded.Id,
                Url = $"drive:{uploaded.Id}"
            };
        }

        var storageKey = $"{productId:N}-{Guid.NewGuid():N}{extension}";
        await localImages.SaveAsync(storageKey, bytes, contentType, cancellationToken);

        return new ProductImage
        {
            FileName = result.FileName,
            ContentType = contentType,
            SizeBytes = buffer.Length,
            Storage = ProductImageStorage.Local,
            StorageKey = storageKey,
            Url = LocalImageKey.ToUrl(storageKey)
        };
    }

    public async Task DeleteAsync(ProductImage image, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (image.Storage == ProductImageStorage.GoogleDrive && !string.IsNullOrWhiteSpace(image.StorageKey))
        {
            await drive.DeleteAsync(
                image.StorageKey,
                await tokens.GetAccessTokenAsync(cancellationToken),
                cancellationToken);
            return;
        }

        if (image.Storage != ProductImageStorage.Local || string.IsNullOrWhiteSpace(image.StorageKey))
            return;

        var key = LocalImageKey.IsValid(image.StorageKey) ? image.StorageKey : LocalImageKey.Sanitize(image.StorageKey);
        await localImages.DeleteAsync(key, cancellationToken);
    }

    private static string NormalizeContentType(string? contentType, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(contentType) && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return contentType.ToLowerInvariant();

        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".heic" or ".heif" => "image/heic",
            _ => "image/jpeg"
        };
    }

    private static string ExtensionFor(string contentType) => contentType switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/heic" or "image/heif" => ".heic",
        _ => ".jpg"
    };
}
