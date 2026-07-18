using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Web.Services;

public sealed class SalesReturnService(ICatalogRepository catalog, ISalesRepository sales, IStockMovementRepository? movements = null)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<SaleReturn> CreateAsync(Sale sale, SaleReturn document, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (sale.Returns.FirstOrDefault(x => x.Id == document.Id) is { } existing) return existing;
            if (sale.Status != SaleStatus.Completed) throw new SaleTransitionException("Частичный возврат доступен только для завершённой продажи.");
            if (string.IsNullOrWhiteSpace(document.Reason)) throw new InvalidOperationException("Укажите причину возврата.");
            var lines = document.Items.Where(x => (x.Quantity ?? 0) > 0).ToList();
            if (lines.Count == 0) throw new InvalidOperationException("Укажите количество хотя бы для одной позиции.");
            if (lines.GroupBy(x => x.SaleItemId).Any(x => x.Count() > 1)) throw new InvalidOperationException("Одна позиция не может повторяться в возврате.");
            if (lines.Any(x => x.Disposition is null)) throw new InvalidOperationException("Для каждой позиции выберите результат возврата.");
            var refund = document.RefundAmountUzs ?? 0;
            var availableRefund = Math.Max(0, sale.TotalUzs - sale.RefundedUzs);
            if (refund < 0 || refund > availableRefund) throw new InvalidOperationException($"Сумма возврата не может превышать {availableRefund:N0} сум.");
            if (lines.All(x => x.Disposition == ReturnDisposition.Rejected) && refund > 0) throw new InvalidOperationException("Для отклонённого возврата сумма возврата денег должна быть равна нулю.");

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

            var movementRecords = new List<StockMovement>();
            foreach (var entry in work)
            {
                var line = entry.Line; var saleItem = entry.SaleItem;
                line.ProductId = saleItem.ProductId; line.ProductVariantId = saleItem.ProductVariantId; line.ProductName = saleItem.ProductName; line.Color = saleItem.Color; line.Size = saleItem.Size;
                if (line.Disposition == ReturnDisposition.Rejected) continue;
                saleItem.ReturnedQuantity += line.Quantity!.Value;
                if (entry.Product is null || entry.Variant is null) continue;
                var product = entry.Product; var variant = entry.Variant;
                variant.Quantity = (variant.Quantity ?? 0) + line.Quantity.Value;
                movementRecords.Add(new StockMovement { Type = StockMovementType.Return, ProductId = product.Id, ProductVariantId = variant.Id, ProductName = product.Name, Sku = product.Sku, Color = variant.Color, Size = variant.Size, QuantityDelta = line.Quantity.Value, SourceType = "SaleReturn", SourceId = sale.Id, SourceNumber = sale.Number, Note = document.Reason.Trim() });
            }

            document.Reason = document.Reason.Trim();
            document.Notes = string.IsNullOrWhiteSpace(document.Notes) ? null : document.Notes.Trim();
            document.Items = lines;
            foreach (var product in products.Values) await catalog.UpsertProductAsync(product, cancellationToken);
            sale.Returns.Add(document);
            if (sale.Items.Where(x => x.SoldQuantity > 0).All(x => x.ReturnedQuantity >= x.SoldQuantity)) sale.Status = SaleStatus.Returned;
            await sales.UpsertSaleAsync(sale, cancellationToken);
            if (movements is not null) await movements.AddRangeAsync(movementRecords, cancellationToken);
            return document;
        }
        finally { Gate.Release(); }
    }

    private sealed record ReturnWork(SaleReturnItem Line, SaleItem SaleItem, Product? Product, ProductVariant? Variant);
}
