using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;

namespace Dona.Crm.App.Services;

public sealed class MauiProductImagePicker : IProductImagePicker
{
    private const long MaxImageBytes = 5 * 1024 * 1024;
    private readonly string imageDirectory = Path.Combine(FileSystem.Current.AppDataDirectory, "product-images");

    public async Task<ProductImage?> PickAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var results = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { SelectionLimit = 1 });
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
        var storageKey = $"{productId:N}-{Guid.NewGuid():N}{extension}";
        Directory.CreateDirectory(imageDirectory);
        var path = Path.Combine(imageDirectory, storageKey);
        await File.WriteAllBytesAsync(path, buffer.ToArray(), cancellationToken);

        return new ProductImage
        {
            FileName = result.FileName,
            ContentType = contentType,
            SizeBytes = buffer.Length,
            Storage = ProductImageStorage.Local,
            StorageKey = storageKey,
            Url = $"data:{contentType};base64,{Convert.ToBase64String(buffer.GetBuffer(), 0, checked((int)buffer.Length))}"
        };
    }

    public Task DeleteAsync(ProductImage image, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (image.Storage != ProductImageStorage.Local || string.IsNullOrWhiteSpace(image.StorageKey))
            return Task.CompletedTask;

        var fileName = Path.GetFileName(image.StorageKey);
        var path = Path.GetFullPath(Path.Combine(imageDirectory, fileName));
        var root = Path.GetFullPath(imageDirectory) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.Ordinal))
            throw new InvalidDataException("Некорректный путь локального изображения.");

        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
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
