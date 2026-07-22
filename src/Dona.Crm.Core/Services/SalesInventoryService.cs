using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Web.Services;

public sealed class SalesInventoryService(ICatalogRepository catalog, ISalesRepository sales, IStockMovementRepository? movements = null)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task ReserveAsync(Sale sale, CancellationToken token = default)
    {
        await Gate.WaitAsync(token);
        try
        {
            EnsureStatus(sale, [null, SaleStatus.Draft, SaleStatus.Reserved, SaleStatus.Paid, SaleStatus.Shipped], "резервирования");
            var lines = await LoadAndValidateAsync(sale, requireStock: true, token);
            var before = Snapshot(lines);
            ApplyReservations(lines);
            await SaveProductsAsync(lines, token);
            if (sale.Status is null or SaleStatus.Draft) sale.Status = SaleStatus.Reserved;
            await sales.UpsertSaleAsync(sale, token);
            await RecordAsync(sale, lines, before, StockMovementType.Reservation, token);
        }
        finally { Gate.Release(); }
    }

    public async Task MarkPaidAsync(Sale sale, CancellationToken token = default)
    {
        await ChangeActiveStatusAsync(sale, SaleStatus.Paid, [SaleStatus.Reserved, SaleStatus.Paid], "оплаты", token);
    }

    public async Task MarkShippedAsync(Sale sale, CancellationToken token = default)
    {
        await ChangeActiveStatusAsync(sale, SaleStatus.Shipped, [SaleStatus.Reserved, SaleStatus.Paid, SaleStatus.Shipped], "отправки", token);
    }

    public async Task CancelAsync(Sale sale, CancellationToken token = default)
    {
        await Gate.WaitAsync(token);
        try
        {
            if (sale.Status == SaleStatus.Cancelled) return;
            EnsureStatus(sale, [null, SaleStatus.Draft, SaleStatus.Reserved, SaleStatus.Paid], "отмены");
            if (sale.Status is null or SaleStatus.Draft && sale.Items.All(x => x.ReservedQuantity == 0)) { sale.Status = SaleStatus.Cancelled; await sales.UpsertSaleAsync(sale, token); return; }
            var lines = await LoadAndValidateAsync(sale, requireStock: false, token);
            var before = Snapshot(lines);
            foreach (var line in lines.Where(x => x.Item.ReservedQuantity > 0))
            {
                line.Variant.ReservedQuantity = Math.Max(0, line.Variant.ReservedQuantity - line.Item.ReservedQuantity);
                line.Item.ReservedQuantity = 0;
            }
            await SaveProductsAsync(lines, token);
            sale.Status = SaleStatus.Cancelled;
            await sales.UpsertSaleAsync(sale, token);
            await RecordAsync(sale, lines, before, StockMovementType.ReservationRelease, token);
        }
        finally { Gate.Release(); }
    }

    public async Task CompleteAsync(Sale sale, CancellationToken token = default)
    {
        await Gate.WaitAsync(token);
        try
        {
            if (sale.Status == SaleStatus.Completed) return;
            EnsureStatus(sale, [SaleStatus.Reserved, SaleStatus.Paid, SaleStatus.Shipped], "завершения");
            var lines = await LoadAndValidateAsync(sale, requireStock: true, token);
            var before = Snapshot(lines);
            foreach (var line in lines)
            {
                var reserveDelta = line.RequiredQuantity - line.Item.ReservedQuantity;
                line.Variant.ReservedQuantity += reserveDelta;
                if ((line.Variant.Quantity ?? 0) < line.RequiredQuantity) throw new InventoryException($"Недостаточно физического остатка для «{line.Item.ProductName}».");
                line.Variant.Quantity = (line.Variant.Quantity ?? 0) - line.RequiredQuantity;
                line.Variant.ReservedQuantity = Math.Max(0, line.Variant.ReservedQuantity - line.RequiredQuantity);
                line.Item.ReservedQuantity = 0;
                line.Item.SoldQuantity += line.RequiredQuantity;
                line.Item.UnitCostUzs ??= line.Product.CostUzs;
            }
            await SaveProductsAsync(lines, token);
            sale.Status = SaleStatus.Completed;
            await sales.UpsertSaleAsync(sale, token);
            await RecordAsync(sale, lines, before, StockMovementType.Sale, token);
        }
        finally { Gate.Release(); }
    }

    public async Task ReturnAsync(Sale sale, CancellationToken token = default)
    {
        await Gate.WaitAsync(token);
        try
        {
            if (sale.Status == SaleStatus.Returned) return;
            EnsureStatus(sale, [SaleStatus.Completed], "возврата");
            var lines = await LoadAndValidateAsync(sale, requireStock: false, token);
            var before = Snapshot(lines);
            foreach (var line in lines.Where(x => x.Item.QuantityToReturn > 0))
            {
                var delta = line.Item.QuantityToReturn;
                line.Variant.Quantity = (line.Variant.Quantity ?? 0) + delta;
                line.Item.ReturnedQuantity += delta;
            }
            await SaveProductsAsync(lines, token);
            sale.Status = SaleStatus.Returned;
            await sales.UpsertSaleAsync(sale, token);
            await RecordAsync(sale, lines, before, StockMovementType.Return, token);
        }
        finally { Gate.Release(); }
    }

    private async Task ChangeActiveStatusAsync(Sale sale, SaleStatus target, SaleStatus?[] allowed, string operation, CancellationToken token)
    {
        await Gate.WaitAsync(token);
        try
        {
            EnsureStatus(sale, allowed, operation);
            var lines = await LoadAndValidateAsync(sale, requireStock: true, token);
            var before = Snapshot(lines);
            ApplyReservations(lines);
            await SaveProductsAsync(lines, token);
            sale.Status = target;
            await sales.UpsertSaleAsync(sale, token);
            await RecordAsync(sale, lines, before, StockMovementType.Reservation, token);
        }
        finally { Gate.Release(); }
    }

    private async Task<List<StockLine>> LoadAndValidateAsync(Sale sale, bool requireStock, CancellationToken token)
    {
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

    private async Task SaveProductsAsync(IEnumerable<StockLine> lines, CancellationToken token)
    {
        foreach (var product in lines.Select(x => x.Product).DistinctBy(x => x.Id)) await catalog.UpsertProductAsync(product, token);
    }

    private static Dictionary<Guid, (int Quantity, int Reserved)> Snapshot(IEnumerable<StockLine> lines) =>
        lines.DistinctBy(x => x.Variant.Id).ToDictionary(x => x.Variant.Id, x => (x.Variant.Quantity ?? 0, x.Variant.ReservedQuantity));

    private Task RecordAsync(Sale sale, IEnumerable<StockLine> lines, IReadOnlyDictionary<Guid, (int Quantity, int Reserved)> before, StockMovementType type, CancellationToken token)
    {
        if (movements is null) return Task.CompletedTask;
        var records = lines.DistinctBy(x => x.Variant.Id).Select(line =>
        {
            var old = before[line.Variant.Id];
            return new StockMovement
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
        }).Where(x => x.QuantityDelta != 0 || x.ReservedDelta != 0).ToList();
        return movements.AddRangeAsync(records, token);
    }

    private static void EnsureStatus(Sale sale, SaleStatus?[] allowed, string operation)
    {
        if (!allowed.Contains(sale.Status)) throw new SaleTransitionException($"Статус «{sale.Status.Display()}» не допускает {operation} заказа.");
    }

    private sealed record StockLine(SaleItem Item, Product Product, ProductVariant Variant, int RequiredQuantity);
}

public sealed class InventoryException(string message) : InvalidOperationException(message);
public sealed class SaleTransitionException(string message) : InvalidOperationException(message);
