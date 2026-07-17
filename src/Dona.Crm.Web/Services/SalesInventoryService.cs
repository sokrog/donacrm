using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Web.Services;

public sealed class SalesInventoryService(ICatalogRepository catalog, ISalesRepository sales)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task ReserveAsync(Sale sale, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            foreach (var item in sale.Items) await SynchronizeItemReservationAsync(item, token);
            sale.Status = SaleStatus.Reserved;
            await sales.UpsertSaleAsync(sale, token);
        }
        finally { _gate.Release(); }
    }

    public async Task CancelAsync(Sale sale, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            foreach (var item in sale.Items.Where(x => x.ReservedQuantity > 0))
            {
                var (product, variant) = await GetVariantAsync(item, token);
                variant.ReservedQuantity = Math.Max(0, variant.ReservedQuantity - item.ReservedQuantity);
                item.ReservedQuantity = 0;
                await catalog.UpsertProductAsync(product, token);
            }
            sale.Status = SaleStatus.Cancelled;
            await sales.UpsertSaleAsync(sale, token);
        }
        finally { _gate.Release(); }
    }

    public async Task CompleteAsync(Sale sale, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            foreach (var item in sale.Items)
            {
                await SynchronizeItemReservationAsync(item, token);
                var (product, variant) = await GetVariantAsync(item, token);
                var delta = Math.Max(0, (item.Quantity ?? 0) - item.SoldQuantity);
                if (delta == 0) continue;
                if (item.ReservedQuantity < delta) throw new InventoryException($"Недостаточно резерва для {item.ProductName}.");
                variant.Quantity = Math.Max(0, (variant.Quantity ?? 0) - delta);
                variant.ReservedQuantity = Math.Max(0, variant.ReservedQuantity - delta);
                item.ReservedQuantity -= delta;
                item.SoldQuantity += delta;
                item.UnitCostUzs ??= product.CostUzs;
                await catalog.UpsertProductAsync(product, token);
            }
            sale.Status = SaleStatus.Completed;
            await sales.UpsertSaleAsync(sale, token);
        }
        finally { _gate.Release(); }
    }

    public async Task ReturnAsync(Sale sale, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            foreach (var item in sale.Items.Where(x => x.QuantityToReturn > 0))
            {
                var (product, variant) = await GetVariantAsync(item, token);
                var delta = item.QuantityToReturn;
                variant.Quantity = (variant.Quantity ?? 0) + delta;
                item.ReturnedQuantity += delta;
                await catalog.UpsertProductAsync(product, token);
            }
            sale.Status = SaleStatus.Returned;
            await sales.UpsertSaleAsync(sale, token);
        }
        finally { _gate.Release(); }
    }

    private async Task SynchronizeItemReservationAsync(SaleItem item, CancellationToken token)
    {
        var (product, variant) = await GetVariantAsync(item, token);
        var desired = Math.Max(0, (item.Quantity ?? 0) - item.SoldQuantity);
        var delta = desired - item.ReservedQuantity;
        if (delta > variant.AvailableQuantity) throw new InventoryException($"Недостаточно товара: {item.ProductName}, {item.Color} {item.Size}. Доступно {variant.AvailableQuantity}, требуется ещё {delta}.");
        variant.ReservedQuantity = Math.Max(0, variant.ReservedQuantity + delta);
        item.ReservedQuantity = desired;
        await catalog.UpsertProductAsync(product, token);
    }

    private async Task<(Product Product, ProductVariant Variant)> GetVariantAsync(SaleItem item, CancellationToken token)
    {
        if (item.ProductId is null || item.ProductVariantId is null) throw new InventoryException($"Для позиции «{item.ProductName}» не выбран вариант.");
        var product = await catalog.GetProductAsync(item.ProductId.Value, token) ?? throw new InventoryException($"Товар «{item.ProductName}» не найден.");
        var variant = product.Variants.FirstOrDefault(x => x.Id == item.ProductVariantId) ?? throw new InventoryException($"Вариант «{item.Color} {item.Size}» не найден.");
        return (product, variant);
    }

}

public sealed class InventoryException(string message) : InvalidOperationException(message);
