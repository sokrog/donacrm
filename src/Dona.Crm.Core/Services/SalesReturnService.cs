using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Web.Services;

public sealed class SalesReturnService(ICatalogRepository catalog, IInventoryStore store, ISalesRepository? sales = null)
{
    public Task<SaleReturn> CreateAsync(Sale sale, SaleReturn document, CancellationToken cancellationToken = default) => EntityRollback.RunAsync(sale, async () =>
    {
        using var _ = await InventoryLock.AcquireAsync(cancellationToken);
        if (sales is not null && await sales.GetSaleAsync(sale.Id, cancellationToken) is { } saved
            && System.Text.Json.JsonSerializer.Serialize(saved) != System.Text.Json.JsonSerializer.Serialize(sale))
            throw new InventoryException("Возвраты продажи уже изменились. Откройте продажу заново.");
        if (sale.Returns.FirstOrDefault(x => x.Id == document.Id) is { } existing) return existing;
        if (sale.Status is not (SaleStatus.Shipped or SaleStatus.Completed)) throw new SaleTransitionException("Товарный возврат доступен после отправки заказа.");
        if (string.IsNullOrWhiteSpace(document.Reason)) throw new InvalidOperationException("Укажите причину возврата.");
        if (document.Items.Any(x => x.Quantity < 0)) throw new InvalidOperationException("Количество возврата не может быть отрицательным.");
        var lines = document.Items.Where(x => (x.Quantity ?? 0) > 0).ToList();
        if (lines.Count == 0) throw new InvalidOperationException("Укажите количество хотя бы для одной позиции.");
        if (lines.GroupBy(x => x.SaleItemId).Any(x => x.Count() > 1)) throw new InvalidOperationException("Одна позиция не может повторяться в возврате.");
        if (lines.Any(x => x.Disposition is null || !Enum.IsDefined(x.Disposition.Value))) throw new InvalidOperationException("Для каждой позиции выберите результат возврата.");
        if (document.DeliveryRefundUzs < 0 || document.DeliveryRefundUzs > SaleReturnCalculator.DeliveryAvailable(sale))
            throw new InvalidOperationException("Возврат доставки не может превышать оставшуюся плату за доставку.");
        if (lines.All(x => x.Disposition == ReturnDisposition.Rejected) && document.DeliveryRefundUzs > 0)
            throw new InvalidOperationException("При отказе в возврате доставка не возмещается этим документом.");

        var products = new Dictionary<Guid, Product>();
        var work = new List<ReturnWork>();
        foreach (var line in lines)
        {
            var saleItem = sale.Items.FirstOrDefault(x => x.Id == line.SaleItemId) ?? throw new InvalidOperationException("Позиция продажи не найдена.");
            var remaining = Math.Max(0, saleItem.SoldQuantity - saleItem.ReturnedQuantity);
            if (line.Quantity is null or <= 0 || line.Quantity > remaining) throw new InvalidOperationException($"Для «{saleItem.ProductName}» можно вернуть не более {remaining} шт.");
            if (line.Disposition != ReturnDisposition.Restock) { work.Add(new ReturnWork(line, saleItem, null, null)); continue; }
            if (saleItem.ProductId is null || saleItem.ProductVariantId is null) throw new InventoryException($"Для «{saleItem.ProductName}» не указан складской вариант.");
            if (!products.TryGetValue(saleItem.ProductId.Value, out var product))
            {
                product = await catalog.GetProductAsync(saleItem.ProductId.Value, cancellationToken) ?? throw new InventoryException($"Товар «{saleItem.ProductName}» больше не существует.");
                products[product.Id] = product;
            }
            var variant = product.Variants.FirstOrDefault(x => x.Id == saleItem.ProductVariantId) ?? throw new InventoryException($"Вариант товара «{saleItem.ProductName}» не найден.");
            work.Add(new ReturnWork(line, saleItem, product, variant));
        }

        document.RefundAmountUzs = SaleReturnCalculator.GoodsRefund(sale, document) + document.DeliveryRefundUzs;
        var movementRecords = new List<StockMovement>();
        foreach (var entry in work)
        {
            var line = entry.Line; var saleItem = entry.SaleItem;
            line.ProductId = saleItem.ProductId; line.ProductVariantId = saleItem.ProductVariantId; line.ProductName = saleItem.ProductName; line.Color = saleItem.Color; line.Size = saleItem.Size;
            if (line.Disposition == ReturnDisposition.Rejected) continue;
            line.LayerAllocations = FifoCostCalculator.PreviewReturn(saleItem.Consumptions,
                sale.Returns.SelectMany(x => x.Items).Where(x => x.SaleItemId == saleItem.Id && x.Disposition != ReturnDisposition.Rejected).SelectMany(x => x.LayerAllocations), line.Quantity!.Value).ToList();
            saleItem.ReturnedQuantity += line.Quantity!.Value;
            if (entry.Product is null || entry.Variant is null) continue;
            var product = entry.Product; var variant = entry.Variant;
            var previousValues = variant.Layers.ToDictionary(x => x.Id, x => x.RemainingValue);
            var restoredCost = StockLayerOperations.Restore(variant, saleItem.Consumptions, line.LayerAllocations);
            foreach (var group in line.LayerAllocations.GroupBy(x => x.LayerId))
            {
                var layer = variant.Layers.Single(x => x.Id == group.Key);
                var originalCost = group.Sum(x => x.OriginalCost ?? 0);
                var restored = layer.RemainingValue - previousValues[layer.Id];
                if (restored is not null && restored != originalCost)
                    product.StockValuations.Add(new(Guid.NewGuid(), document.Id, document.CreatedAt,
                        StockValuationReason.ReturnRevaluation, layer.Id, layer.PurchaseItemId, 0, layer.ValuationRevision,
                        originalCost, restored, 0, originalCost - restored));
            }
            movementRecords.Add(new StockMovement { Type = StockMovementType.Return, ProductId = product.Id, ProductVariantId = variant.Id, ProductName = product.Name, Sku = product.Sku, Color = variant.Color, Size = variant.Size, QuantityDelta = line.Quantity.Value, SourceType = "SaleReturn", SourceId = sale.Id, SourceNumber = sale.Number, Note = document.Reason.Trim() });
            StockLayerOperations.SetValue(movementRecords[^1], restoredCost, 1);
            movementRecords[^1].ReturnAllocations = line.LayerAllocations.ToList();
        }

        document.Reason = document.Reason.Trim();
        document.Notes = string.IsNullOrWhiteSpace(document.Notes) ? null : document.Notes.Trim();
        document.Items = lines;
        sale.Returns.Add(document);
        if (sale.Items.Where(x => x.SoldQuantity > 0).All(x => x.ReturnedQuantity >= x.SoldQuantity)) sale.Status = SaleStatus.Returned;
        await store.CommitAsync(InventoryCommit.Create(products: products.Values, sales: [sale], movements: movementRecords), cancellationToken);
        return document;
    });

    private sealed record ReturnWork(SaleReturnItem Line, SaleItem SaleItem, Product? Product, ProductVariant? Variant);
}
