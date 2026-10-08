using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Web.Services;

public sealed class SalesInventoryService(ICatalogRepository catalog, IInventoryStore store, IBusinessSettingsRepository? settings = null, ISalesRepository? sales = null)
{
    public async Task<IReadOnlyDictionary<Guid, StockCostSummary>> PreviewAsync(Sale sale, CancellationToken token = default)
    {
        using var gate = await InventoryLock.AcquireAsync(token);
        var lines = await LoadAndValidateAsync(sale, requireStock: true, token);
        return lines.ToDictionary(x => x.Item.Id, x => x.RequiredQuantity == 0
            ? FifoCostCalculator.Cost(x.Item.Consumptions)
            : FifoCostCalculator.Cost(FifoCostCalculator.Preview(x.Variant, x.RequiredQuantity, x.Item.ReservedQuantity)));
    }

    public Task ReserveAsync(Sale sale, CancellationToken token = default) => EntityRollback.RunAsync(sale, async () =>
    {
        using var _ = await InventoryLock.AcquireAsync(token);
        EnsureStatus(sale, [null, SaleStatus.Draft, SaleStatus.Reserved, SaleStatus.Paid, SaleStatus.Shipped], "резервирования");
        var lines = await LoadAndValidateAsync(sale, requireStock: true, token);
        var before = Snapshot(lines);
        ApplyReservations(lines);
        if (sale.Status is null or SaleStatus.Draft) sale.Status = SaleStatus.Reserved;
        await CommitAsync(sale, lines, before, StockMovementType.Reservation, token);
    });

    public Task MarkPaidAsync(Sale sale, CancellationToken token = default) =>
        ChangeActiveStatusAsync(sale, SaleStatus.Paid, [SaleStatus.Reserved, SaleStatus.Paid], "оплаты", token);

    public Task MarkShippedAsync(Sale sale, CancellationToken token = default) =>
        ChangeActiveStatusAsync(sale, SaleStatus.Shipped, [SaleStatus.Reserved, SaleStatus.Paid, SaleStatus.Shipped], "отправки", token);

    public Task CancelAsync(Sale sale, CancellationToken token = default) => EntityRollback.RunAsync(sale, async () =>
    {
        using var _ = await InventoryLock.AcquireAsync(token);
        if (sale.Status == SaleStatus.Cancelled) return;
        EnsureStatus(sale, [null, SaleStatus.Draft, SaleStatus.Reserved, SaleStatus.Paid], "отмены");
        if (sale.Status is null or SaleStatus.Draft && sale.Items.All(x => x.ReservedQuantity == 0))
        {
            sale.Status = SaleStatus.Cancelled;
            await store.CommitAsync(InventoryCommit.Create(sales: [sale]), token);
            return;
        }
        var lines = await LoadAndValidateAsync(sale, requireStock: false, token);
        var before = Snapshot(lines);
        foreach (var line in lines.Where(x => x.Item.ReservedQuantity > 0))
        {
            line.Variant.ReservedQuantity = Math.Max(0, line.Variant.ReservedQuantity - line.Item.ReservedQuantity);
            line.Item.ReservedQuantity = 0;
        }
        sale.Status = SaleStatus.Cancelled;
        await CommitAsync(sale, lines, before, StockMovementType.ReservationRelease, token);
    });

    public Task CompleteAsync(Sale sale, CancellationToken token = default) => EntityRollback.RunAsync(sale, async () =>
    {
        using var _ = await InventoryLock.AcquireAsync(token);
        if (sale.Status == SaleStatus.Completed) return;
        EnsureStatus(sale, [SaleStatus.Reserved, SaleStatus.Paid, SaleStatus.Shipped], "завершения");
        var lines = await LoadAndValidateAsync(sale, requireStock: true, token);
        if (settings is not null && (await settings.GetAsync(token)).PreventSalesBelowCost)
            foreach (var line in lines.Where(x => x.RequiredQuantity > 0))
            {
                var cost = FifoCostCalculator.Cost(FifoCostCalculator.Preview(line.Variant, line.RequiredQuantity, line.Item.ReservedQuantity));
                var revenue = (line.Item.UnitPriceUzs ?? 0) * line.RequiredQuantity *
                    (sale.SubtotalUzs <= 0 ? 0 : Math.Max(0, sale.SubtotalUzs - (sale.DiscountUzs ?? 0)) / sale.SubtotalUzs);
                if (cost.TotalValue is null) throw new InventoryException("Себестоимость неизвестна. Проверка продажи ниже себестоимости невозможна.");
                if (revenue < cost.TotalValue) throw new InventoryException($"Цена «{line.Item.ProductName}» с учётом скидки ниже себестоимости FIFO.");
            }
        var before = Snapshot(lines);
        foreach (var line in lines)
        {
            if (line.RequiredQuantity == 0) continue;
            line.Item.Consumptions.AddRange(FifoCostCalculator.Consume(line.Variant, line.RequiredQuantity, line.Item.ReservedQuantity));
            line.Item.ReservedQuantity = 0;
            line.Item.SoldQuantity += line.RequiredQuantity;
            line.Item.UnitCostUzs = FifoCostCalculator.Cost(line.Item.Consumptions).TotalValue / line.Item.SoldQuantity;
        }
        sale.Status = SaleStatus.Completed;
        sale.CompletedAt ??= DateTimeOffset.UtcNow;
        await CommitAsync(sale, lines, before, StockMovementType.Sale, token);
    });

    public Task ReturnAsync(Sale sale, CancellationToken token = default)
    {
        if (sale.Status == SaleStatus.Returned) return Task.CompletedTask;
        EnsureStatus(sale, [SaleStatus.Completed], "возврата");
        var document = new SaleReturn
        {
            Reason = "Полный возврат", RefundAmountUzs = Math.Max(0, sale.TotalUzs - sale.RefundedUzs),
            Items = sale.Items.Where(x => x.QuantityToReturn > 0).Select(x => new SaleReturnItem
                { SaleItemId = x.Id, Quantity = x.QuantityToReturn, Disposition = ReturnDisposition.Restock }).ToList()
        };
        return new SalesReturnService(catalog, store, sales).CreateAsync(sale, document, token);
    }

    private Task ChangeActiveStatusAsync(Sale sale, SaleStatus target, SaleStatus?[] allowed, string operation, CancellationToken token) => EntityRollback.RunAsync(sale, async () =>
    {
        using var _ = await InventoryLock.AcquireAsync(token);
        EnsureStatus(sale, allowed, operation);
        var lines = await LoadAndValidateAsync(sale, requireStock: true, token);
        var before = Snapshot(lines);
        ApplyReservations(lines);
        sale.Status = target;
        await CommitAsync(sale, lines, before, StockMovementType.Reservation, token);
    });

    private async Task<List<StockLine>> LoadAndValidateAsync(Sale sale, bool requireStock, CancellationToken token)
    {
        if (sales is not null && await sales.GetSaleAsync(sale.Id, token) is { } saved)
        {
            var oldState = saved.Items.Where(x => x.ReservedQuantity != 0 || x.SoldQuantity != 0 || x.ReturnedQuantity != 0)
                .Select(x => (x.Id, x.ReservedQuantity, x.SoldQuantity, x.ReturnedQuantity)).OrderBy(x => x.Id);
            var newState = sale.Items.Where(x => x.ReservedQuantity != 0 || x.SoldQuantity != 0 || x.ReturnedQuantity != 0)
                .Select(x => (x.Id, x.ReservedQuantity, x.SoldQuantity, x.ReturnedQuantity)).OrderBy(x => x.Id);
            if (saved.Status != sale.Status || !oldState.SequenceEqual(newState))
                throw new InventoryException("Продажа уже изменена. Откройте её заново перед складской операцией.");
        }
        if (sale.Items.Count == 0) throw new InventoryException("Добавьте хотя бы одну позицию в заказ.");
        if (sale.Items.Any(x => x.Quantity is null or <= 0)) throw new InventoryException("Количество каждой позиции должно быть больше нуля.");
        if (sale.Items.Any(x => x.UnitPriceUzs is null or < 0)) throw new InventoryException("Укажите неотрицательную цену каждой позиции.");
        var duplicate = sale.Items.Where(x => x.ProductVariantId is not null).GroupBy(x => x.ProductVariantId).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null) throw new InventoryException("Один вариант товара нельзя добавлять в заказ несколькими строками. Объедините количество в одной позиции.");
        if (sale.Items.Any(x => x.ProductId is null || x.ProductVariantId is null)) throw new InventoryException("Для каждой позиции выберите товар и вариант.");

        var products = new Dictionary<Guid, Product>();
        foreach (var productId in sale.Items.Select(x => x.ProductId!.Value).Distinct())
            products[productId] = await catalog.GetProductAsync(productId, token) ?? throw new InventoryException("Один из товаров заказа больше не существует.");

        var lines = new List<StockLine>();
        foreach (var item in sale.Items)
        {
            var product = products[item.ProductId!.Value];
            var variant = product.Variants.FirstOrDefault(x => x.Id == item.ProductVariantId) ?? throw new InventoryException($"Вариант «{item.Color} {item.Size}» товара «{item.ProductName}» не найден.");
            StockLayerOperations.PrepareEmpty(variant);
            var required = Math.Max(0, item.Quantity!.Value - item.SoldQuantity);
            var availableForThisSale = variant.AvailableQuantity + item.ReservedQuantity;
            if (requireStock && required > availableForThisSale) throw new InventoryException($"Недостаточно товара: «{item.ProductName}», {item.Color} {item.Size}. Доступно {availableForThisSale}, требуется {required}.");
            lines.Add(new StockLine(item, product, variant, required));
        }
        return lines;
    }

    private static void ApplyReservations(IEnumerable<StockLine> lines)
    {
        foreach (var line in lines)
        {
            var delta = line.RequiredQuantity - line.Item.ReservedQuantity;
            line.Variant.ReservedQuantity = Math.Max(0, line.Variant.ReservedQuantity + delta);
            line.Item.ReservedQuantity = line.RequiredQuantity;
        }
    }

    private static Dictionary<Guid, (int Quantity, int Reserved)> Snapshot(IEnumerable<StockLine> lines) =>
        lines.DistinctBy(x => x.Variant.Id).ToDictionary(x => x.Variant.Id, x => (x.Variant.Quantity ?? 0, x.Variant.ReservedQuantity));

    private Task CommitAsync(Sale sale, IEnumerable<StockLine> lines, IReadOnlyDictionary<Guid, (int Quantity, int Reserved)> before, StockMovementType type, CancellationToken token)
    {
        var records = lines.DistinctBy(x => x.Variant.Id).Select(line =>
        {
            var old = before[line.Variant.Id];
            var movement = new StockMovement
            {
                Type = type,
                ProductId = line.Product.Id,
                ProductVariantId = line.Variant.Id,
                ProductName = line.Product.Name,
                Sku = line.Product.Sku,
                Color = line.Variant.Color,
                Size = line.Variant.Size,
                QuantityDelta = (line.Variant.Quantity ?? 0) - old.Quantity,
                ReservedDelta = line.Variant.ReservedQuantity - old.Reserved,
                SourceType = "Sale",
                SourceId = sale.Id,
                SourceNumber = sale.Number
            };
            if (type == StockMovementType.Sale)
            {
                movement.Consumptions = line.Item.Consumptions.ToList();
                StockLayerOperations.SetValue(movement, FifoCostCalculator.Cost(movement.Consumptions), -1);
            }
            else movement.ValueDelta = 0;
            return movement;
        }).Where(x => x.QuantityDelta != 0 || x.ReservedDelta != 0).ToList();
        return store.CommitAsync(InventoryCommit.Create(products: lines.Select(x => x.Product).DistinctBy(x => x.Id), sales: [sale], movements: records), token);
    }

    private static void EnsureStatus(Sale sale, SaleStatus?[] allowed, string operation)
    {
        if (!allowed.Contains(sale.Status)) throw new SaleTransitionException($"Статус «{sale.Status.Display()}» не допускает {operation} заказа.");
    }

    private sealed record StockLine(SaleItem Item, Product Product, ProductVariant Variant, int RequiredQuantity);
}

public sealed class InventoryException(string message) : InvalidOperationException(message);
public sealed class SaleTransitionException(string message) : InvalidOperationException(message);
