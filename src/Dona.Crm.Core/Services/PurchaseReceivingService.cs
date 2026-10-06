using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Dona.Crm.Web.Services;

public sealed record PurchaseReceiptInput(Guid PurchaseItemId, int ReceivedQuantity, int DefectQuantity);
public sealed record PurchaseReceiptResult(Guid ReceiptId, int AddedUnits, int UpdatedProducts, int SkippedItems);

public sealed class PurchaseReceivingService(ICatalogRepository catalog, IInventoryStore store, ICommerceRepository? commerce = null)
{
    public Task SaveAsync(Purchase purchase, CancellationToken cancellationToken = default) => EntityRollback.RunAsync(purchase, async () =>
    {
        using var gate = await InventoryLock.AcquireAsync(cancellationToken);
        Validator.ValidateObject(purchase, new ValidationContext(purchase), true);
        if (purchase.CurrencyCode == "UZS") purchase.CnyRateUzs = 1;
        var repository = commerce ?? throw new InvalidOperationException("Хранилище закупок недоступно.");
        var saved = await repository.GetPurchaseAsync(purchase.Id, cancellationToken);
        if (saved is not null && JsonSerializer.Serialize(saved.CostRevisions) != JsonSerializer.Serialize(purchase.CostRevisions))
            throw new InvalidOperationException("Расходы закупки изменились. Откройте закупку заново перед сохранением.");
        if (saved is not null && JsonSerializer.Serialize(saved.Receipts) != JsonSerializer.Serialize(purchase.Receipts))
            throw new InvalidOperationException("Приёмка изменилась. Откройте закупку заново перед сохранением.");
        if (saved is not null)
            foreach (var received in saved.Items.Where(x => (x.ReceivedQuantity ?? 0) > 0))
            {
                var edited = purchase.Items.FirstOrDefault(x => x.Id == received.Id);
                if (edited is null || edited.ProductId != received.ProductId || edited.ProductVariantId != received.ProductVariantId || (edited.Quantity ?? 0) < received.ReceivedQuantity)
                    throw new InvalidOperationException("Нельзя удалить или заменить принятую позицию либо уменьшить количество ниже принятого.");
            }
        var changed = saved is null || CostSignature(saved) != CostSignature(purchase);
        if (purchase.Receipts.Count > 0 && !purchase.HasCompleteCostInputs)
            throw new InvalidOperationException("Для пересчета принятой закупки заполните цены, курсы и расходы.");
        var products = new Dictionary<Guid, Product>();
        var corrections = new List<ProductCostHistoryEntry>();
        if (changed)
        {
            if (purchase.HasCompleteCostInputs)
                purchase.CostRevisions.Add(new(DateTimeOffset.UtcNow, purchase.TotalCostUzs, purchase.IsCostFinalized));
            foreach (var receipt in purchase.Receipts)
            {
                var (costs, _) = await BuildHistoryAsync(purchase, receipt, products, cancellationToken);
                corrections.AddRange(costs);
            }
            foreach (var product in products.Values.Where(x => x.CostPurchaseId == purchase.Id)) ApplyProductCost(purchase, product);
        }
        await store.CommitAsync(InventoryCommit.Create(
            products: products.Values.Where(x => x.CostPurchaseId == purchase.Id), purchases: [purchase]) with { CostCorrections = corrections }, cancellationToken);
    });

    private static string CostSignature(Purchase purchase) => JsonSerializer.Serialize(new
    {
        purchase.CurrencyCode, purchase.CnyRateUzs, purchase.AgentCommissionPercent,
        purchase.InternationalShippingUzs, purchase.OtherCostsUzs, purchase.Expenses, purchase.IsCostFinalized,
        Items = purchase.Items.Select(x => new { x.Id, x.Quantity, x.UnitPriceCny, x.UnitWeightKg })
    });

    private static void ApplyProductCost(Purchase purchase, Product product)
    {
        var items = purchase.Items.Where(x => x.ProductId == product.Id && x.StockedQuantity > 0).ToList();
        var quantity = items.Sum(x => x.Quantity ?? 0);
        if (quantity <= 0) return;
        product.PurchasePriceCny = items.Sum(x => (x.UnitPriceCny ?? 0) * (x.Quantity ?? 0)) / quantity;
        product.PurchaseCurrencyCode = purchase.CurrencyCode;
        product.CnyRateUzs = purchase.CurrencyCode == "UZS" ? 1 : purchase.CnyRateUzs;
        product.AgentCommissionPercent = 0;
        product.DeliveryCostUzs = items.Sum(x => purchase.ItemLandedCostUzs(x) - purchase.ItemGoodsCostUzs(x)) / quantity;
        product.CostPurchaseId = purchase.Id;
    }

    public Task<PurchaseReceiptResult> ReceiveAsync(Purchase purchase, IEnumerable<PurchaseReceiptInput> input, Guid? receiptId = null, DateTimeOffset? receivedAt = null, string? note = null, CancellationToken cancellationToken = default) => EntityRollback.RunAsync(purchase, async () =>
    {
        using var _ = await InventoryLock.AcquireAsync(cancellationToken);
        if (purchase.Status == PurchaseStatus.Cancelled) throw new InvalidOperationException("Отменённую закупку нельзя принимать.");
        if (purchase.Expenses.Count > 0 && !purchase.HasCompleteCostInputs)
            throw new InvalidOperationException("Перед приемкой заполните цены, курсы и расходы.");
        if (string.IsNullOrWhiteSpace(purchase.Number)) throw new InvalidOperationException("Укажите номер закупки.");
        var products = new Dictionary<Guid, Product>();
        if (receiptId is not null && purchase.Receipts.FirstOrDefault(x => x.Id == receiptId) is { } existing)
        {
            var (replayCosts, replayRate) = await BuildHistoryAsync(purchase, existing, products, cancellationToken);
            await store.CommitAsync(InventoryCommit.Create(productCosts: replayCosts, exchangeRate: replayRate), cancellationToken);
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
            var product = await LoadProductAsync(item.ProductId.Value, products, cancellationToken);
            if (product is null) { skipped++; receipt.Lines.Add(line); continue; }
            var variant = item.ProductVariantId is not null ? product.Variants.FirstOrDefault(x => x.Id == item.ProductVariantId) : null;
            variant ??= product.Variants.FirstOrDefault(x => x.Color.Equals(item.Color, StringComparison.OrdinalIgnoreCase) && x.Size.Equals(item.Size, StringComparison.OrdinalIgnoreCase));
            if (variant is null) { variant = new ProductVariant { Color = item.Color, Size = item.Size }; product.Variants.Add(variant); }
            variant.Quantity = (variant.Quantity ?? 0) + accepted;
            item.ProductVariantId = variant.Id;
            item.StockedQuantity += accepted;
            line.ProductVariantId = variant.Id;
            line.StockedQuantity = accepted;
            if (accepted > 0) ApplyProductCost(purchase, product);
            if (accepted != 0) movementRecords.Add(new StockMovement { Type = StockMovementType.PurchaseReceipt, ProductId = product.Id, ProductVariantId = variant.Id, ProductName = product.Name, Sku = product.Sku, Color = variant.Color, Size = variant.Size, QuantityDelta = accepted, SourceType = "Purchase", SourceId = purchase.Id, SourceNumber = purchase.Number, Note = $"Приёмка {receipt.ReceivedAt.ToLocalTime():dd.MM.yyyy}" });
            added += accepted; updated++;
            receipt.Lines.Add(line);
        }

        purchase.Receipts.Add(receipt);
        purchase.Status = purchase.Items.Count > 0 && purchase.Items.All(x => (x.ReceivedQuantity ?? 0) >= (x.Quantity ?? 0)) ? PurchaseStatus.Received : PurchaseStatus.PartiallyReceived;
        var (costs, rate) = await BuildHistoryAsync(purchase, receipt, products, cancellationToken);
        await store.CommitAsync(InventoryCommit.Create(products: products.Values, purchases: [purchase], movements: movementRecords, productCosts: costs, exchangeRate: rate), cancellationToken);
        return new PurchaseReceiptResult(receipt.Id, added, updated, skipped);
    });

    private async Task<Product?> LoadProductAsync(Guid id, Dictionary<Guid, Product> cache, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(id, out var cached)) return cached;
        var product = await catalog.GetProductAsync(id, cancellationToken);
        if (product is not null) cache[id] = product;
        return product;
    }

    private async Task<(List<ProductCostHistoryEntry> Costs, ExchangeRateHistoryEntry? Rate)> BuildHistoryAsync(Purchase purchase, PurchaseReceipt receipt, Dictionary<Guid, Product> products, CancellationToken cancellationToken)
    {
        var entries = new List<ProductCostHistoryEntry>();
        foreach (var line in receipt.Lines.Where(x => x.StockedQuantity > 0 && x.ProductId is not null))
        {
            var item = purchase.Items.FirstOrDefault(x => x.Id == line.PurchaseItemId);
            if (item is null) continue;
            var product = await LoadProductAsync(line.ProductId!.Value, products, cancellationToken);
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
                CurrencyCode = CurrencyCodes.Normalize(purchase.CurrencyCode, "CNY"),
                CnyRateUzs = purchase.CnyRateUzs ?? 0,
                UnitLandedCostUzs = purchase.ItemUnitLandedCostUzs(item)
            });
        }
        var rate = purchase.CnyRateUzs is > 0 ? new ExchangeRateHistoryEntry
        {
            Id = receipt.Id,
            RecordedAt = receipt.ReceivedAt,
            RateUzs = purchase.CnyRateUzs.Value,
            Currency = CurrencyCodes.Normalize(purchase.CurrencyCode, "CNY"),
            PurchaseId = purchase.Id,
            ReceiptId = receipt.Id,
            PurchaseNumber = purchase.Number,
            SupplierName = purchase.SupplierName ?? string.Empty
        } : null;
        return (entries, rate);
    }
}
