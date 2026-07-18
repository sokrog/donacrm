using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Web.Services;

public sealed record PurchaseReceiptInput(Guid PurchaseItemId, int ReceivedQuantity, int DefectQuantity);
public sealed record PurchaseReceiptResult(Guid ReceiptId, int AddedUnits, int UpdatedProducts, int SkippedItems);

public sealed class PurchaseReceivingService(ICatalogRepository catalog, ICommerceRepository commerce, IStockMovementRepository? movements = null, IPurchaseHistoryRepository? history = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<PurchaseReceiptResult> ReceiveAsync(Purchase purchase, IEnumerable<PurchaseReceiptInput> input, Guid? receiptId = null, DateTimeOffset? receivedAt = null, string? note = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (purchase.Status == PurchaseStatus.Cancelled) throw new InvalidOperationException("Отменённую закупку нельзя принимать.");
            if (string.IsNullOrWhiteSpace(purchase.Number)) throw new InvalidOperationException("Укажите номер закупки.");
            if (receiptId is not null && purchase.Receipts.FirstOrDefault(x => x.Id == receiptId) is { } existing)
            {
                await RecordHistoryAsync(purchase, existing, cancellationToken);
                return new PurchaseReceiptResult(existing.Id, existing.StockedQuantity, existing.Lines.Count(x => x.StockedQuantity > 0), existing.Lines.Count(x => x.ProductId is null));
            }
            var submitted = input.Where(x => x.ReceivedQuantity > 0 || x.DefectQuantity > 0).ToList();
            if (submitted.Count == 0) throw new InvalidOperationException("Укажите полученное количество хотя бы для одной позиции.");
            if (submitted.GroupBy(x => x.PurchaseItemId).Any(x => x.Count() > 1)) throw new InvalidOperationException("Одна позиция не может повторяться в приёмке.");

            var receipt = new PurchaseReceipt { Id = receiptId ?? Guid.NewGuid(), ReceivedAt = receivedAt ?? DateTimeOffset.UtcNow, Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim() };
            var movementRecords = new List<StockMovement>();
            var added = 0; var updated = 0; var skipped = 0;

            foreach (var entry in submitted)
            {
                var item = purchase.Items.FirstOrDefault(x => x.Id == entry.PurchaseItemId) ?? throw new InvalidOperationException("Позиция закупки не найдена.");
                if (entry.ReceivedQuantity <= 0) throw new InvalidOperationException($"Для «{item.ProductName}» полученное количество должно быть больше нуля.");
                if (entry.DefectQuantity < 0 || entry.DefectQuantity > entry.ReceivedQuantity) throw new InvalidOperationException($"Брак для «{item.ProductName}» не может превышать полученное количество.");
                var remaining = Math.Max(0, (item.Quantity ?? 0) - (item.ReceivedQuantity ?? 0));
                if (entry.ReceivedQuantity > remaining) throw new InvalidOperationException($"Для «{item.ProductName}» осталось принять {remaining} шт.");

                var line = new PurchaseReceiptLine { PurchaseItemId = item.Id, ProductId = item.ProductId, ProductVariantId = item.ProductVariantId, ProductName = item.ProductName, Color = item.Color, Size = item.Size, ReceivedQuantity = entry.ReceivedQuantity, DefectQuantity = entry.DefectQuantity };
                item.ReceivedQuantity = (item.ReceivedQuantity ?? 0) + entry.ReceivedQuantity;
                item.DefectQuantity = (item.DefectQuantity ?? 0) + entry.DefectQuantity;
                var accepted = entry.ReceivedQuantity - entry.DefectQuantity;

                if (item.ProductId is null) { skipped++; receipt.Lines.Add(line); continue; }
                var product = await catalog.GetProductAsync(item.ProductId.Value, cancellationToken);
                if (product is null) { skipped++; receipt.Lines.Add(line); continue; }
                var variant = item.ProductVariantId is not null ? product.Variants.FirstOrDefault(x => x.Id == item.ProductVariantId) : null;
                variant ??= product.Variants.FirstOrDefault(x => x.Color.Equals(item.Color, StringComparison.OrdinalIgnoreCase) && x.Size.Equals(item.Size, StringComparison.OrdinalIgnoreCase));
                if (variant is null) { variant = new ProductVariant { Color = item.Color, Size = item.Size }; product.Variants.Add(variant); }
                variant.Quantity = (variant.Quantity ?? 0) + accepted;
                item.ProductVariantId = variant.Id;
                item.StockedQuantity += accepted;
                line.ProductVariantId = variant.Id;
                line.StockedQuantity = accepted;
                product.PurchasePriceCny = item.UnitPriceCny;
                product.CnyRateUzs = purchase.CnyRateUzs;
                product.AgentCommissionPercent = purchase.AgentCommissionPercent;
                product.DeliveryCostUzs = (item.Quantity ?? 0) == 0 ? 0 : Math.Round((purchase.ItemShippingUzs(item) + purchase.ItemOtherCostsUzs(item)) / item.Quantity!.Value);
                await catalog.UpsertProductAsync(product, cancellationToken);
                if (accepted != 0) movementRecords.Add(new StockMovement { Type = StockMovementType.PurchaseReceipt, ProductId = product.Id, ProductVariantId = variant.Id, ProductName = product.Name, Sku = product.Sku, Color = variant.Color, Size = variant.Size, QuantityDelta = accepted, SourceType = "Purchase", SourceId = purchase.Id, SourceNumber = purchase.Number, Note = $"Приёмка {receipt.ReceivedAt.ToLocalTime():dd.MM.yyyy}" });
                added += accepted; updated++;
                receipt.Lines.Add(line);
            }

            purchase.Receipts.Add(receipt);
            purchase.Status = purchase.Items.Count > 0 && purchase.Items.All(x => (x.ReceivedQuantity ?? 0) >= (x.Quantity ?? 0)) ? PurchaseStatus.Received : PurchaseStatus.PartiallyReceived;
            await commerce.UpsertPurchaseAsync(purchase, cancellationToken);
            if (movements is not null) await movements.AddRangeAsync(movementRecords, cancellationToken);
            await RecordHistoryAsync(purchase, receipt, cancellationToken);
            return new PurchaseReceiptResult(receipt.Id, added, updated, skipped);
        }
        finally { _gate.Release(); }
    }

    private async Task RecordHistoryAsync(Purchase purchase, PurchaseReceipt receipt, CancellationToken cancellationToken)
    {
        if (history is null) return;
        var entries = new List<ProductCostHistoryEntry>();
        foreach (var line in receipt.Lines.Where(x => x.StockedQuantity > 0 && x.ProductId is not null))
        {
            var item = purchase.Items.FirstOrDefault(x => x.Id == line.PurchaseItemId);
            if (item is null) continue;
            var product = await catalog.GetProductAsync(line.ProductId!.Value, cancellationToken);
            entries.Add(new ProductCostHistoryEntry
            {
                Id = line.Id,
                RecordedAt = receipt.ReceivedAt,
                ProductId = line.ProductId.Value,
                ProductVariantId = line.ProductVariantId,
                ProductName = product?.Name ?? line.ProductName,
                Sku = product?.Sku ?? string.Empty,
                Color = line.Color,
                Size = line.Size,
                PurchaseId = purchase.Id,
                ReceiptId = receipt.Id,
                PurchaseNumber = purchase.Number,
                SupplierId = purchase.SupplierId,
                SupplierName = purchase.SupplierName ?? string.Empty,
                Quantity = line.StockedQuantity,
                UnitPriceCny = item.UnitPriceCny ?? 0,
                CnyRateUzs = purchase.CnyRateUzs ?? 0,
                UnitLandedCostUzs = purchase.ItemUnitLandedCostUzs(item)
            });
        }
        var rate = purchase.CnyRateUzs is > 0 ? new ExchangeRateHistoryEntry
        {
            Id = receipt.Id,
            RecordedAt = receipt.ReceivedAt,
            RateUzs = purchase.CnyRateUzs.Value,
            PurchaseId = purchase.Id,
            ReceiptId = receipt.Id,
            PurchaseNumber = purchase.Number,
            SupplierName = purchase.SupplierName ?? string.Empty
        } : null;
        await history.AddAsync(entries, rate, cancellationToken);
    }
}
