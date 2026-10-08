using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;
using System.Text.Json;

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
        EnsureStatus(sale, [null, SaleStatus.Draft, SaleStatus.Reserved], "резервирования");
        var lines = await LoadAndValidateAsync(sale, requireStock: true, token);
        var before = Snapshot(lines);
        ApplyReservations(lines);
        if (sale.Status is null or SaleStatus.Draft) sale.Status = SaleStatus.Reserved;
        await CommitAsync(sale, lines, before, StockMovementType.Reservation, token);
    });

    public Task CancelAsync(Sale sale, CancellationToken token = default) => CancelAsync(sale, null, token);

    public Task CancelAsync(Sale sale, decimal? deliveryRefundUzs, CancellationToken token = default) => EntityRollback.RunAsync(sale, async () =>
    {
        using var _ = await InventoryLock.AcquireAsync(token);
        await EnsureCurrentAsync(sale, token);
        if (sale.Status == SaleStatus.Cancelled) return;
        EnsureStatus(sale, [null, SaleStatus.Draft, SaleStatus.Reserved], "отмены");
        var deliveryRefund = deliveryRefundUzs ?? sale.DeliveryChargeUzs ?? 0;
        if (deliveryRefund < 0 || deliveryRefund > (sale.DeliveryChargeUzs ?? 0))
            throw new InvalidOperationException("Возврат доставки должен быть от нуля до платы за доставку.");
        sale.CancellationDeliveryRefundUzs = deliveryRefund;
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

    public Task MarkShippedAsync(Sale sale, CancellationToken token = default) => EntityRollback.RunAsync(sale, async () =>
    {
        using var _ = await InventoryLock.AcquireAsync(token);
        await EnsureCurrentAsync(sale, token);
        if ((sale.Status is SaleStatus.Shipped or SaleStatus.Completed) && sale.ShippedAt is not null) return;
        EnsureStatus(sale, [SaleStatus.Reserved], "отправки");
        var lines = await LoadAndValidateAsync(sale, requireStock: true, token);
        if (settings is not null && (await settings.GetAsync(token)).PreventSalesBelowCost)
            foreach (var line in lines.Where(x => x.RequiredQuantity > 0))
            {
                var cost = FifoCostCalculator.Cost(FifoCostCalculator.Preview(line.Variant, line.RequiredQuantity, line.Item.ReservedQuantity));
                var revenue = (line.Item.UnitPriceUzs ?? 0) * line.RequiredQuantity *
                    (sale.SubtotalUzs <= 0 ? 0 : Math.Max(0, sale.SubtotalUzs - sale.AppliedDiscountUzs) / sale.SubtotalUzs);
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
        sale.Status = SaleStatus.Shipped;
        sale.ShippedAt = DateTimeOffset.UtcNow;
        await CommitAsync(sale, lines, before, StockMovementType.Sale, token);
    });

    public Task ReturnAsync(Sale sale, CancellationToken token = default)
    {
        if (sale.Status == SaleStatus.Returned) return Task.CompletedTask;
        EnsureStatus(sale, [SaleStatus.Shipped, SaleStatus.Completed], "возврата");
        var document = new SaleReturn
        {
            Reason = "Полный возврат", RefundAmountUzs = Math.Max(0, sale.TotalUzs - sale.RefundedUzs),
            Items = sale.Items.Where(x => x.QuantityToReturn > 0).Select(x => new SaleReturnItem
                { SaleItemId = x.Id, Quantity = x.QuantityToReturn, Disposition = ReturnDisposition.Restock }).ToList()
        };
        return new SalesReturnService(catalog, store, sales).CreateAsync(sale, document, token);
    }

    public Task CompleteAsync(Sale sale, CancellationToken token = default) => EntityRollback.RunAsync(sale, async () =>
    {
        using var _ = await InventoryLock.AcquireAsync(token);
        await EnsureCurrentAsync(sale, token);
        if (sale.Status == SaleStatus.Completed) return;
        EnsureStatus(sale, [SaleStatus.Shipped], "завершения");
        await LoadAndValidateAsync(sale, requireStock: false, token);
        if (sale.ShippedAt is null || sale.Items.Any(x => x.SoldQuantity != x.Quantity || x.ReservedQuantity != 0))
            throw new InventoryException("Сначала оформите отправку всех позиций заказа.");
        sale.Status = SaleStatus.Completed;
        sale.CompletedAt = DateTimeOffset.UtcNow;
        await store.CommitAsync(InventoryCommit.Create(sales: [sale]), token);
    });

    /// <summary>Reverses inventory and hides the sale in one atomic commit; keeps its audit record.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken token = default)
    {
        if (sales is null) throw new InvalidOperationException("Хранилище продаж недоступно.");
        using var gate = await InventoryLock.AcquireAsync(token);
        var saved = await sales.GetSaleAsync(id, token) ?? throw new InvalidOperationException("Продажа не найдена.");
        if (saved.DeletedAt is not null) return;
        var deleted = JsonSerializer.Deserialize<Sale>(JsonSerializer.Serialize(saved))!;
        var products = new Dictionary<Guid, Product>();
        var movements = new List<StockMovement>();
        var at = DateTimeOffset.UtcNow;
        foreach (var item in deleted.Items)
        {
            var sold = item.SoldQuantity;
            if (sold == 0 && deleted.Status is SaleStatus.Shipped or SaleStatus.Completed)
                sold = item.Quantity ?? 0; // Historical sales may predate the explicit issue counter.
            var quantity = Math.Max(0, sold - item.ReturnedQuantity);
            var defects = deleted.Returns.SelectMany(x => x.Items)
                .Where(x => x.SaleItemId == item.Id && x.Disposition == ReturnDisposition.Defect)
                .SelectMany(x => x.LayerAllocations).ToList();
            if (defects.Count > 0)
                movements.Add(new StockMovement
                {
                    Type = StockMovementType.Adjustment, CreatedAt = at,
                    ProductId = item.ProductId ?? Guid.Empty, ProductVariantId = item.ProductVariantId ?? Guid.Empty,
                    ProductName = item.ProductName, Color = item.Color, Size = item.Size,
                    SourceType = "SaleDeletionDefect", SourceId = deleted.Id, SourceNumber = deleted.Number,
                    Note = "Брак, принятый до удаления продажи; остаток уже списан", ValueDelta = 0,
                    Consumptions = defects.Select(x => new LayerConsumption(Guid.NewGuid(), x.LayerId, x.Quantity,
                        x.OriginalCost / x.Quantity, x.OriginalCost,
                        item.Consumptions.First(c => c.Id == x.ConsumptionId).ValuationRevision)).ToList()
                });
            if (quantity == 0 && item.ReservedQuantity == 0) continue;
            if (item.ProductId is not { } productId || item.ProductVariantId is not { } variantId)
                throw new InventoryException("Не найден товар или вариант для восстановления остатка.");
            if (!products.TryGetValue(productId, out var product))
            {
                product = await catalog.GetProductAsync(productId, token) ?? throw new InventoryException("Товар продажи не найден.");
                products.Add(productId, product);
            }
            var variant = product.Variants.SingleOrDefault(x => x.Id == variantId) ?? throw new InventoryException("Вариант продажи не найден.");
            StockLayerOperations.PrepareEmpty(variant);
            if (item.ReservedQuantity > variant.ReservedQuantity)
                throw new InventoryException("Резерв продажи не соответствует остатку. Обновите данные склада.");
            var movement = new StockMovement
            {
                Type = quantity > 0 ? StockMovementType.Return : StockMovementType.ReservationRelease,
                CreatedAt = at, ProductId = product.Id, ProductVariantId = variant.Id,
                ProductName = product.Name, Sku = product.Sku, Color = variant.Color, Size = variant.Size,
                QuantityDelta = quantity, ReservedDelta = -item.ReservedQuantity,
                SourceType = "SaleDeletion", SourceId = deleted.Id, SourceNumber = deleted.Number,
                Note = "Удаление ошибочной продажи", ValueDelta = 0
            };
            variant.ReservedQuantity -= item.ReservedQuantity;
            item.ReservedQuantity = 0;
            if (quantity > 0 && item.Consumptions.Count > 0)
            {
                var accepted = deleted.Returns.SelectMany(x => x.Items)
                    .Where(x => x.SaleItemId == item.Id && x.Disposition != ReturnDisposition.Rejected)
                    .SelectMany(x => x.LayerAllocations);
                var allocations = FifoCostCalculator.PreviewReturn(item.Consumptions, accepted, quantity);
                var previousValues = variant.Layers.ToDictionary(x => x.Id, x => x.RemainingValue);
                var cost = StockLayerOperations.Restore(variant, item.Consumptions, allocations);
                StockLayerOperations.SetValue(movement, cost, 1);
                movement.ReturnAllocations = allocations.ToList();
                foreach (var group in allocations.GroupBy(x => x.LayerId))
                {
                    var layer = variant.Layers.Single(x => x.Id == group.Key);
                    var originalCost = group.Sum(x => x.OriginalCost ?? 0);
                    var restored = layer.RemainingValue - previousValues[layer.Id];
                    if (restored is not null && restored != originalCost)
                        product.StockValuations.Add(new(Guid.NewGuid(), deleted.Id, at, StockValuationReason.ReturnRevaluation,
                            layer.Id, layer.PurchaseItemId, 0, layer.ValuationRevision, originalCost, restored, 0, originalCost - restored));
                }
            }
            else if (quantity > 0)
            {
                var cost = item.UnitCostUzs * quantity;
                StockLayerOperations.Add(variant, quantity, cost, StockLayerSource.LegacyReturn, at, deleted.Number);
                StockLayerOperations.SetValue(movement, new StockCostSummary(cost ?? 0, cost is null ? quantity : 0), 1);
            }
            movements.Add(movement);
        }
        deleted.DeletedAt = at;
        await store.CommitAsync(InventoryCommit.Create(products: products.Values, sales: [deleted], movements: movements), token);
    }

    private async Task EnsureCurrentAsync(Sale sale, CancellationToken token)
    {
        if (sales is not null && await sales.GetSaleAsync(sale.Id, token) is { } saved)
        {
            if (saved.DeletedAt is not null) throw new InventoryException("Продажа удалена. Откройте список продаж заново.");
            var oldState = saved.Items.Where(x => x.ReservedQuantity != 0 || x.SoldQuantity != 0 || x.ReturnedQuantity != 0)
                .Select(x => (x.Id, x.ProductId, x.ProductVariantId, x.ReservedQuantity, x.SoldQuantity, x.ReturnedQuantity, Costs: JsonSerializer.Serialize(x.Consumptions))).OrderBy(x => x.Id);
            var newState = sale.Items.Where(x => x.ReservedQuantity != 0 || x.SoldQuantity != 0 || x.ReturnedQuantity != 0)
                .Select(x => (x.Id, x.ProductId, x.ProductVariantId, x.ReservedQuantity, x.SoldQuantity, x.ReturnedQuantity, Costs: JsonSerializer.Serialize(x.Consumptions))).OrderBy(x => x.Id);
            if (saved.Status != sale.Status || !oldState.SequenceEqual(newState)
                || JsonSerializer.Serialize(saved.Payments) != JsonSerializer.Serialize(sale.Payments)
                || JsonSerializer.Serialize(saved.Returns) != JsonSerializer.Serialize(sale.Returns))
                throw new InventoryException("Продажа уже изменена. Откройте её заново перед складской операцией.");
        }
    }

    private async Task<List<StockLine>> LoadAndValidateAsync(Sale sale, bool requireStock, CancellationToken token)
    {
        await EnsureCurrentAsync(sale, token);
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
