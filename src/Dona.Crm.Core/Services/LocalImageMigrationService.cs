using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Web.Services;

/// <summary>Переносит байты фотографий из data:-ссылок внутри сущностей в ILocalImageStore и заменяет ссылку на local:&lt;key&gt;.</summary>
public static class LocalImageMigration
{
    /// <summary>Конвертирует локальные изображения с data:-ссылкой. Возвращает число изменённых изображений; битые изображения пропускаются.</summary>
    public static async Task<int> ConvertAsync(
        IEnumerable<ProductImage> images,
        ILocalImageStore store,
        CancellationToken cancellationToken = default)
    {
        var converted = 0;
        foreach (var image in images)
        {
            if (image.Storage != ProductImageStorage.Local
                || !LocalImageKey.TryParseDataUrl(image.Url, out var contentType, out var content)
                || content.Length == 0)
                continue;
            try
            {
                var key = LocalImageKey.IsValid(image.StorageKey) ? image.StorageKey : LocalImageKey.Sanitize(image.StorageKey);
                if (await store.ReadAsync(key, cancellationToken) is null)
                    await store.SaveAsync(key, content, contentType, cancellationToken);
                image.StorageKey = key;
                image.Url = LocalImageKey.ToUrl(key);
                if (image.SizeBytes <= 0)
                    image.SizeBytes = content.Length;
                if (string.IsNullOrWhiteSpace(image.ContentType))
                    image.ContentType = contentType;
                converted++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Одна повреждённая фотография не должна блокировать остальные.
            }
        }
        return converted;
    }

    /// <summary>Убирает дублирующую data:-ссылку из Product.ImageUrl после конвертации изображений товара.</summary>
    public static bool FixProductImageUrl(Product product)
    {
        if (product.ImageUrl is null || !product.ImageUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return false;
        var main = product.PrimaryImage?.Url;
        product.ImageUrl = main is not null && !main.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? main : null;
        return true;
    }

    /// <summary>Конвертирует все изображения снимка (используется при восстановлении старых копий).</summary>
    public static async Task<int> ConvertSnapshotAsync(
        DonaSyncSnapshot snapshot,
        ILocalImageStore store,
        CancellationToken cancellationToken = default)
    {
        var converted = await ConvertAsync(LocalImageKey.EnumerateImages(snapshot), store, cancellationToken);
        foreach (var product in snapshot.Products)
            FixProductImageUrl(product);
        return converted;
    }
}

/// <summary>Однократное фоновое исправление данных при старте приложения; идемпотентно и дёшево, если переносить нечего.</summary>
public sealed class LocalImageMigrationService(
    ICatalogRepository catalog,
    IMarketingRepository marketing,
    ILocalImageStore store)
{
    private readonly object gate = new();
    private Task<int>? migration;

    /// <summary>Запускает миграцию один раз за жизнь сервиса; ошибки не пробрасываются.</summary>
    public Task<int> EnsureMigratedAsync()
    {
        lock (gate)
            return migration ??= MigrateSafelyAsync();
    }

    public async Task<int> MigrateAsync(CancellationToken cancellationToken = default)
    {
        var converted = 0;
        var pending = (await catalog.GetProductsAsync(cancellationToken))
            .Where(product => product.Images.Any(image => image.Url?.StartsWith("data:", StringComparison.OrdinalIgnoreCase) == true) ||
                              product.ImageUrl?.StartsWith("data:", StringComparison.OrdinalIgnoreCase) == true)
            .Select(product => product.Id)
            .ToList();
        foreach (var productId in pending)
        {
            // Товар перечитывается под складской блокировкой, чтобы не затереть остатки, изменённые продажей после старта.
            using var _ = await InventoryLock.AcquireAsync(cancellationToken);
            var product = await catalog.GetProductAsync(productId, cancellationToken);
            if (product is null)
                continue;
            var count = await LocalImageMigration.ConvertAsync(product.Images, store, cancellationToken);
            var fixedUrl = LocalImageMigration.FixProductImageUrl(product);
            if (count == 0 && !fixedUrl)
                continue;
            await TryPersistAsync(() => catalog.UpsertProductAsync(product, cancellationToken), cancellationToken);
            converted += count;
        }

        foreach (var collection in await marketing.GetCollectionsAsync(cancellationToken))
        {
            var count = await LocalImageMigration.ConvertAsync(collection.Images, store, cancellationToken);
            if (count == 0)
                continue;
            await TryPersistAsync(() => marketing.UpsertCollectionAsync(collection, cancellationToken), cancellationToken);
            converted += count;
        }

        foreach (var outfit in await marketing.GetOutfitsAsync(cancellationToken))
        {
            var count = await LocalImageMigration.ConvertAsync(outfit.Images, store, cancellationToken);
            if (count == 0)
                continue;
            await TryPersistAsync(() => marketing.UpsertOutfitAsync(outfit, cancellationToken), cancellationToken);
            converted += count;
        }

        return converted;
    }

    private async Task<int> MigrateSafelyAsync()
    {
        try
        {
            return await MigrateAsync();
        }
        catch
        {
            return 0;
        }
    }

    private static async Task TryPersistAsync(Func<Task> persist, CancellationToken cancellationToken)
    {
        try
        {
            await persist();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Сущность останется со старой data:-ссылкой, перенос повторится при следующем запуске.
        }
    }
}
