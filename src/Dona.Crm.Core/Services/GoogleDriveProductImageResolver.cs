using System.Collections.Concurrent;
using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Services;

public sealed class GoogleDriveProductImageResolver(
    GoogleDriveFileClient drive,
    IGoogleAccessTokenProvider tokens) : IProductImageResolver
{
    private readonly ConcurrentDictionary<string, string> cache = new(StringComparer.Ordinal);

    public async Task<string?> ResolveAsync(ProductImage image, CancellationToken cancellationToken = default)
    {
        if (image.Storage != ProductImageStorage.GoogleDrive)
            return image.Url;
        if (string.IsNullOrWhiteSpace(image.StorageKey))
            return null;
        if (cache.TryGetValue(image.StorageKey, out var cached))
            return cached;

        var download = await drive.DownloadAsync(
            image.StorageKey,
            await tokens.GetAccessTokenAsync(cancellationToken),
            cancellationToken);
        var source = $"data:{download.ContentType};base64,{Convert.ToBase64String(download.Content)}";
        cache[image.StorageKey] = source;
        return source;
    }
}
