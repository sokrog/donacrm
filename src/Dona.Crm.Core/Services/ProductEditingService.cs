using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Web.Services;

/// <summary>
/// Сохранение карточки товара без права менять складские остатки в обход журнала движений:
/// остатки и резервы берутся из хранилища, а не из устаревшей копии редактора.
/// </summary>
public sealed class ProductEditingService(
    ICatalogRepository catalog,
    ISalesRepository sales,
    ICommerceRepository commerce,
    IBusinessSettingsRepository settings,
    ProductStatusService statuses,
    IInventoryStore store)
{
    /// <param name="original">
    /// Состояние товара на момент открытия редактора. Из редактора переносятся только изменённые пользователем поля,
    /// варианты, появившиеся позже, не считаются удалёнными. Продажная цена всегда берётся из хранилища:
    /// её меняет только отдельная операция назначения цен. null — переносить редактируемые поля
    /// и считать удалённым всё, чего нет в копии.
    /// </param>
    public Task<Product> SaveAsync(Product edited, Product? original = null, CancellationToken cancellationToken = default) =>
        SaveCoreAsync(edited, archive: false, original, cancellationToken);

    public Task<Product> ArchiveAsync(Product edited, Product? original = null, CancellationToken cancellationToken = default) =>
        SaveCoreAsync(edited, archive: true, original, cancellationToken);

    /// <summary>Причина, по которой вариант нельзя удалить, или null, если удаление допустимо.</summary>
    public static string? VariantRemovalBlocker(ProductVariant stored, IEnumerable<Sale> allSales, IEnumerable<Purchase> allPurchases)
    {
        var name = VariantName(stored);
        const string hint = "Заархивируйте товар или обнулите остаток через «Корректировка остатка».";
        if ((stored.Quantity ?? 0) > 0) return $"Нельзя удалить вариант «{name}»: на складе {stored.Quantity} шт. {hint}";
        if (stored.ReservedQuantity > 0) return $"Нельзя удалить вариант «{name}»: он зарезервирован ({stored.ReservedQuantity} шт.). {hint}";
        if (stored.Layers.Count > 0) return $"Нельзя удалить вариант «{name}»: у него есть история партий. {hint}";
        if (allSales.Any(sale => sale.Items.Any(item => item.ProductVariantId == stored.Id))) return $"Нельзя удалить вариант «{name}»: он используется в продажах. {hint}";
        if (allPurchases.Any(purchase => purchase.Items.Any(item => item.ProductVariantId == stored.Id))) return $"Нельзя удалить вариант «{name}»: он используется в закупках. {hint}";
        return null;
    }

    private async Task<Product> SaveCoreAsync(Product edited, bool archive, Product? original, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edited);
        var validation = edited.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(edited)).FirstOrDefault();
        if (validation is not null) throw new InvalidOperationException(validation.ErrorMessage);
        var loadedVariantIds = original?.Variants.Select(variant => variant.Id).ToHashSet();
        using var _ = await InventoryLock.AcquireAsync(cancellationToken);
        var sku = edited.Sku.Trim();
        var all = await catalog.GetProductsAsync(cancellationToken);
        var duplicate = all.FirstOrDefault(product => product.Id != edited.Id && string.Equals(product.Sku.Trim(), sku, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null) throw new InvalidOperationException($"SKU «{sku}» уже используется товаром «{duplicate.Name}».");
        if (edited.Variants.Any(variant => variant.Quantity is < 0)) throw new InvalidOperationException("Остаток варианта не может быть отрицательным.");
        if (edited.PreferredSupplierId is { } preferred && original?.PreferredSupplierId != preferred
            && !(await commerce.GetSuppliersAsync(cancellationToken)).Any(x => x.Id == preferred))
            throw new InvalidOperationException("Предпочтительный поставщик не найден. Выберите поставщика из справочника.");

        var stored = all.FirstOrDefault(product => product.Id == edited.Id);
        var product = stored ?? new Product { Id = edited.Id, CreatedAt = edited.CreatedAt };
        var baseline = stored is null ? null : original;
        product.Sku = sku;
        product.Name = Merge(baseline, value => value.Name, edited, product);
        product.Category = Merge(baseline, value => value.Category, edited, product);
        product.Gender = Merge(baseline, value => value.Gender, edited, product);
        product.Season = Merge(baseline, value => value.Season, edited, product);
        product.PreferredSupplierId = Merge(baseline, value => value.PreferredSupplierId, edited, product);
        product.SourceUrl = Merge(baseline, value => value.SourceUrl, edited, product);
        product.ImageUrl = edited.ImageUrl;
        product.Notes = Merge(baseline, value => value.Notes, edited, product);
        product.UnitWeightKg = Merge(baseline, value => value.UnitWeightKg, edited, product);
        // Price changes belong exclusively to ProductPricingService; stale card copies cannot overwrite them.
        product.SellingPriceUzs = stored?.SellingPriceUzs;
        product.Images = edited.Images;
        product.Status = archive ? ProductStatus.Archived : edited.Status;

        var storedVariants = (stored?.Variants ?? []).ToDictionary(variant => variant.Id);
        var variants = new List<ProductVariant>();
        foreach (var variant in edited.Variants)
        {
            if (storedVariants.TryGetValue(variant.Id, out var current))
            {
                current.Color = variant.Color;
                current.Size = variant.Size;
                variants.Add(current);
                continue;
            }
            variants.Add(new ProductVariant { Id = variant.Id, Color = variant.Color, Size = variant.Size, Quantity = 0, StockLayerVersion = FifoCostCalculator.CurrentVersion });
        }

        var missing = storedVariants.Values.Where(variant => variants.All(kept => kept.Id != variant.Id)).ToList();
        if (loadedVariantIds is not null)
            variants.AddRange(missing.Where(variant => !loadedVariantIds.Contains(variant.Id)));
        var removed = missing.Where(variant => loadedVariantIds is null || loadedVariantIds.Contains(variant.Id)).ToList();
        if (removed.Count > 0)
        {
            var allSales = await sales.GetSalesAsync(cancellationToken);
            var allPurchases = await commerce.GetPurchasesAsync(cancellationToken);
            foreach (var variant in removed)
                if (VariantRemovalBlocker(variant, allSales, allPurchases) is { } reason) throw new InvalidOperationException(reason);
        }

        product.Variants = variants;
        if (!archive) product.Status = statuses.Calculate(product, await settings.GetAsync(cancellationToken));
        await store.CommitAsync(InventoryCommit.Create(products: [product]), cancellationToken);
        return product;
    }

    // Поле, которое пользователь не трогал, берётся из хранилища: его могли изменить, пока был открыт редактор.
    private static T Merge<T>(Product? baseline, Func<Product, T> field, Product edited, Product current) =>
        baseline is not null && EqualityComparer<T>.Default.Equals(field(edited), field(baseline)) ? field(current) : field(edited);

    private static string VariantName(ProductVariant variant) =>
        string.Join(" · ", new[] { variant.Color, variant.Size }.Where(value => !string.IsNullOrWhiteSpace(value))) is { Length: > 0 } name ? name : "Без названия";
}
