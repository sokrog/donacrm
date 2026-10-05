using System.Collections.Concurrent;
using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Services;

/// <summary>Превращает ProductImage в источник для img: Drive, локальное хранилище (local:), старые data:-ссылки и внешние URL.</summary>
public sealed class ProductImageResolver(
    GoogleDriveFileClient drive,
    IGoogleAccessTokenProvider tokens,
    ILocalImageStore localImages) : IProductImageResolver
{
    public const int MaxCachedImages = 100;
    private readonly ConcurrentDictionary<string, string> driveCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> localCache = new(StringComparer.Ordinal);
    private readonly LinkedList<string> localOrder = new();
    private readonly object localLock = new();

    public async Task<string?> ResolveAsync(ProductImage image, CancellationToken cancellationToken = default)
    {
        switch (image.Storage)
        {
            case ProductImageStorage.GoogleDrive:
                return await ResolveDriveAsync(image, cancellationToken);
            case ProductImageStorage.Local when LocalImageKey.TryParseUrl(image.Url, out var key):
                return await ResolveLocalAsync(key, cancellationToken);
            case ProductImageStorage.Local when image.Url.StartsWith("local:", StringComparison.Ordinal):
                return null;
            default:
                return image.Url;
        }
    }

    private async Task<string?> ResolveDriveAsync(ProductImage image, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(image.StorageKey))
            return null;
        if (driveCache.TryGetValue(image.StorageKey, out var cached))
            return cached;

        var download = await drive.DownloadAsync(
            image.StorageKey,
            await tokens.GetAccessTokenAsync(cancellationToken),
            cancellationToken);
        var source = LocalImageKey.ToDataUrl(download.Content, download.ContentType);
        driveCache[image.StorageKey] = source;
        return source;
    }

    /// <summary>Возвращает null, если байты фотографии отсутствуют на этом устройстве.</summary>
    private async Task<string?> ResolveLocalAsync(string key, CancellationToken cancellationToken)
    {
        lock (localLock)
        {
            if (localCache.TryGetValue(key, out var cached))
            {
                localOrder.Remove(key);
                localOrder.AddFirst(key);
                return cached;
            }
        }

        var stored = await localImages.ReadAsync(key, cancellationToken);
        if (stored is null)
            return null;
        var source = LocalImageKey.ToDataUrl(stored.Content, stored.ContentType);
        lock (localLock)
        {
            if (!localCache.ContainsKey(key))
            {
                localCache[key] = source;
                localOrder.AddFirst(key);
                while (localCache.Count > MaxCachedImages && localOrder.Last is { } oldest)
                {
                    localCache.Remove(oldest.Value);
                    localOrder.RemoveLast();
                }
            }
        }
        return source;
    }
}
