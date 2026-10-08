using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Dona.Crm.Web.Services;

public sealed record PurchaseReceiptInput(Guid PurchaseItemId, int ReceivedQuantity, int DefectQuantity);
public sealed record PurchaseReceiptResult(Guid ReceiptId, int AddedUnits, int UpdatedProducts, int SkippedItems);
public sealed record PurchaseShortageInput(Guid PurchaseItemId, decimal SupplierRefund);

public sealed class PurchaseReceivingService(ICatalogRepository catalog, IInventoryStore store, ICommerceRepository? commerce = null)
{
    public Task SaveAsync(Purchase purchase, CancellationToken cancellationToken = default) => EntityRollback.RunAsync(purchase, async () =>
    {
        using var gate = await InventoryLock.AcquireAsync(cancellationToken);
        Validator.ValidateObject(purchase, new ValidationContext(purchase), true);
        if (purchase.CurrencyCode == "UZS") purchase.CnyRateUzs = 1;
        var repository = commerce ?? throw new InvalidOperationException("Хранилище закупок недоступно.");
        var saved = await repository.GetPurchaseAsync(purchase.Id, cancellationToken);
        if (saved is not null && (saved.ClosedAt != purchase.ClosedAt || saved.ClosingOperationId != purchase.ClosingOperationId
            || JsonSerializer.Serialize(saved.ShortageSettlements) != JsonSerializer.Serialize(purchase.ShortageSettlements)
            || JsonSerializer.Serialize(saved.CompensationCorrections) != JsonSerializer.Serialize(purchase.CompensationCorrections)
            || JsonSerializer.Serialize(saved.LateReceipts) != JsonSerializer.Serialize(purchase.LateReceipts)
            || JsonSerializer.Serialize(saved.StockValuations) != JsonSerializer.Serialize(purchase.StockValuations)))
            throw new InvalidOperationException("Документы оценки или закрытия изменились. Откройте закупку заново.");
        if (saved is not null && JsonSerializer.Serialize(saved.CostRevisions) != JsonSerializer.Serialize(purchase.CostRevisions))
            throw new InvalidOperationException("Расходы закупки изменились. Откройте закупку заново перед сохранением.");
        if (saved is not null && JsonSerializer.Serialize(saved.Receipts) != JsonSerializer.Serialize(purchase.Receipts))
            throw new InvalidOperationException("Приёмка изменилась. Откройте закупку заново перед сохранением.");
        if (saved is not null)
        {
            if (saved.ClosedAt is not null && !saved.Items.Select(x => (x.Id, x.ProductId, x.ProductVariantId, x.Quantity))
                .SequenceEqual(purchase.Items.Select(x => (x.Id, x.ProductId, x.ProductVariantId, x.Quantity))))
                throw new InvalidOperationException("Нельзя менять состав и количество закрытой закупки.");
            foreach (var received in saved.Items.Where(x => (x.ReceivedQuantity ?? 0) > 0))
            {
                var edited = purchase.Items.FirstOrDefault(x => x.Id == received.Id);
                if (edited is null || edited.ProductId != received.ProductId || edited.ProductVariantId != received.ProductVariantId || (edited.Quantity ?? 0) < received.ReceivedQuantity)
                    throw new InvalidOperationException("Нельзя удалить или заменить принятую позицию либо уменьшить количество ниже принятого.");
            }
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
                foreach (var line in receipt.Lines.Where(x => x.ReceivedQuantity > 0 && x.ReceivedQuantity == x.DefectQuantity))
                    RecordDefect(purchase, receipt, line, ReceiptValue(purchase, receipt, line), DateTimeOffset.UtcNow);
            }
            foreach (var settlement in purchase.ShortageSettlements)
            {
                var item = purchase.Items.Single(x => x.Id == settlement.PurchaseItemId);
                var value = FifoCostCalculator.Allocate(purchase.ItemLandedCostUzs(item), item.Quantity!.Value,
                    item.ReceivedQuantity ?? 0, purchase.UnresolvedShortage(settlement));
                if (value < purchase.CurrentRefund(settlement))
                    throw new InvalidOperationException("Новая стоимость недостачи меньше учтённой компенсации. Сначала исправьте компенсацию отдельным документом.");
                var previous = purchase.StockValuations.LastOrDefault(x => x.Reason == StockValuationReason.Shortage && x.OperationId == settlement.Id);
                if (previous?.NewValue != value)
                    purchase.StockValuations.Add(new(Guid.NewGuid(), settlement.Id, DateTimeOffset.UtcNow, StockValuationReason.Shortage,
                        null, item.Id, 0, 0, previous?.NewValue, value, 0, value - (previous?.NewValue ?? 0)));
            }
            foreach (var product in products.Values)
            {
                foreach (var variant in product.Variants)
                foreach (var layer in variant.Layers.Where(x => x.PurchaseId == purchase.Id))
                {
                    var receipt = purchase.Receipts.Single(x => x.Id == layer.ReceiptId);
                    var line = receipt.Lines.Single(x => x.Id == layer.ReceiptLineId);
                    var value = ReceiptValue(purchase, receipt, line);
                    if (value == layer.InitialValue) continue;
                    var remaining = FifoCostCalculator.Allocate(value, layer.InitialQuantity, 0, layer.RemainingQuantity);
                    purchase.StockValuations.Add(new(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
                        layer.InitialValue is null ? StockValuationReason.InitialValuation : StockValuationReason.Revaluation,
                        layer.Id, layer.PurchaseItemId, layer.ValuationRevision, layer.ValuationRevision + 1,
                        layer.InitialValue, value, remaining - (layer.RemainingValue ?? 0),
                        value - remaining - ((layer.InitialValue ?? 0) - (layer.RemainingValue ?? 0))));
                    layer.InitialValue = value;
                    layer.RemainingValue = remaining;
                    layer.ValuationRevision++;
                }
                if (product.CostPurchaseId == purchase.Id) ApplyProductCost(purchase, product);
            }
        }
        await store.CommitAsync(InventoryCommit.Create(
            products: products.Values, purchases: [purchase]) with { CostCorrections = corrections }, cancellationToken);
    });

    private static string CostSignature(Purchase purchase) => JsonSerializer.Serialize(new
    {
        purchase.CurrencyCode, purchase.CnyRateUzs, purchase.AgentCommissionPercent,
        purchase.InternationalShippingUzs, purchase.OtherCostsUzs, purchase.Expenses, purchase.IsCostFinalized,
        Items = purchase.Items.Select(x => new { x.Id, x.Quantity, x.UnitPriceCny, x.UnitWeightKg })
    });

    public async Task<PurchaseLateReceipt> ReceiveLateAsync(Guid purchaseId, Guid settlementId, Guid operationId,
        int expectedMissing, int quantity, int defects, string reason, CancellationToken token = default)
    {
        using var gate = await InventoryLock.AcquireAsync(token);
        if (operationId == Guid.Empty || quantity <= 0 || defects < 0 || defects > quantity || string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Укажите количество, допустимый брак и причину поздней поставки.");
        var repository = commerce ?? throw new InvalidOperationException("Хранилище закупок недоступно.");
        var purchase = await repository.GetPurchaseAsync(purchaseId, token) ?? throw new InvalidOperationException("Закупка не найдена.");
        var replay = purchase.LateReceipts.SingleOrDefault(x => x.Id == operationId);
        if (replay is not null)
        {
            if (replay.SettlementId != settlementId || replay.Quantity != quantity || replay.Defects != defects || replay.Reason != reason.Trim())
                throw new InvalidOperationException("Документ уже сохранён с другими данными.");
            return replay;
        }
        if (purchase.ClosedAt is null || !purchase.HasCompleteCostInputs)
            throw new InvalidOperationException("Нужна закрытая закупка с полной стоимостью.");
        var settlement = purchase.ShortageSettlements.SingleOrDefault(x => x.Id == settlementId)
            ?? throw new InvalidOperationException("Недостача не найдена.");
        var missing = purchase.UnresolvedShortage(settlement);
        if (missing != expectedMissing || quantity > missing)
            throw new InvalidOperationException("Количество недостачи изменилось или превышено. Откройте документ заново.");
        var item = purchase.Items.Single(x => x.Id == settlement.PurchaseItemId);
        var total = purchase.ItemLandedCostUzs(item);
        var received = item.ReceivedQuantity ?? 0;
        var receiptValue = FifoCostCalculator.Allocate(total, item.Quantity!.Value, received, quantity)!.Value;
        var remainingValue = FifoCostCalculator.Allocate(total, item.Quantity.Value, received + quantity, missing - quantity)!.Value;
        if (purchase.CurrentRefund(settlement) > remainingValue)
            throw new InvalidOperationException("Компенсация превышает стоимость оставшейся недостачи. Сначала оформите её фактическое уменьшение отдельным исправлением.");
        var product = item.ProductId is { } productId ? await catalog.GetProductAsync(productId, token) : null;
        if (product is null) throw new InvalidOperationException("Товар не найден.");
        var variant = product.Variants.SingleOrDefault(x => x.Id == item.ProductVariantId)
            ?? throw new InvalidOperationException("Исходный вариант товара не найден.");
        StockLayerOperations.PrepareEmpty(variant);
        var now = DateTimeOffset.UtcNow;
        var receipt = new PurchaseReceipt { Id = operationId, ReceivedAt = now, Note = reason.Trim() };
        var line = new PurchaseReceiptLine { PurchaseItemId = item.Id, ProductId = product.Id, ProductVariantId = variant.Id,
            ProductName = product.Name, Color = variant.Color, Size = variant.Size, ReceivedQuantity = quantity,
            DefectQuantity = defects, StockedQuantity = quantity - defects };
        receipt.Lines.Add(line);
        var correction = new PurchaseLateReceipt(operationId, settlementId, receipt.Id, now, quantity, defects, reason.Trim());
        var movements = new List<StockMovement>();
        if (line.StockedQuantity > 0)
        {
            var layer = StockLayerOperations.Add(variant, line.StockedQuantity, receiptValue, StockLayerSource.PurchaseReceipt, now, purchase.Number, line.Id);
            layer.PurchaseId = purchase.Id; layer.PurchaseItemId = item.Id; layer.ReceiptId = receipt.Id; layer.ReceiptLineId = line.Id;
            var movement = new StockMovement { Type = StockMovementType.PurchaseReceipt, ProductId = product.Id, ProductVariantId = variant.Id,
                ProductName = product.Name, Sku = product.Sku, Color = variant.Color, Size = variant.Size, QuantityDelta = line.StockedQuantity,
                SourceType = "PurchaseLateReceipt", SourceId = operationId, SourceNumber = purchase.Number, Note = reason.Trim() };
            StockLayerOperations.SetValue(movement, new(receiptValue, 0), 1);
            movements.Add(movement);
        }
        else RecordDefect(purchase, receipt, line, receiptValue, now);
        var oldValue = purchase.StockValuations.LastOrDefault(x => x.Reason == StockValuationReason.Shortage && x.OperationId == settlement.Id)?.NewValue
            ?? settlement.AllocatedCost ?? throw new InvalidOperationException("Стоимость недостачи неизвестна.");
        purchase.StockValuations.Add(new(Guid.NewGuid(), settlement.Id, now, StockValuationReason.Shortage, null, item.Id,
            0, 0, oldValue, remainingValue, 0, remainingValue - oldValue));
        purchase.LateReceipts.Add(correction);
        purchase.Receipts.Add(receipt);
        item.ReceivedQuantity = received + quantity;
        item.DefectQuantity = (item.DefectQuantity ?? 0) + defects;
        item.StockedQuantity += line.StockedQuantity;
        purchase.Status = purchase.Items.All(x => (x.ReceivedQuantity ?? 0) >= (x.Quantity ?? 0))
            ? PurchaseStatus.Received : PurchaseStatus.PartiallyReceived;
        var products = new Dictionary<Guid, Product> { [product.Id] = product };
        var (costs, rate) = await BuildHistoryAsync(purchase, receipt, products, token);
        await store.CommitAsync(InventoryCommit.Create(products: [product], purchases: [purchase], movements: movements, productCosts: costs, exchangeRate: rate), token);
        return correction;
    }

    public async Task<PurchaseCompensationCorrection> CorrectCompensationAsync(Guid purchaseId, Guid settlementId,
        Guid operationId, decimal expectedRefund, decimal newRefund, string reason, CancellationToken token = default)
    {
        using var gate = await InventoryLock.AcquireAsync(token);
        if (operationId == Guid.Empty || newRefund < 0 || decimal.Round(newRefund, 2) != newRefund || string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Укажите итоговую компенсацию с точностью до двух знаков и причину исправления.");
        var repository = commerce ?? throw new InvalidOperationException("Хранилище закупок недоступно.");
        var purchase = await repository.GetPurchaseAsync(purchaseId, token) ?? throw new InvalidOperationException("Закупка не найдена.");
        var previous = purchase.CompensationCorrections.SingleOrDefault(x => x.Id == operationId);
        if (previous is not null)
        {
            if (previous.SettlementId != settlementId || previous.PreviousRefund != expectedRefund || previous.NewRefund != newRefund || previous.Reason != reason.Trim())
                throw new InvalidOperationException("Исправление с этим номером уже сохранено с другими данными.");
            return previous;
        }
        if (purchase.ClosedAt is null) throw new InvalidOperationException("Сначала закройте закупку и учтите недостачу.");
        var settlement = purchase.ShortageSettlements.SingleOrDefault(x => x.Id == settlementId)
            ?? throw new InvalidOperationException("Документ недостачи не найден.");
        var current = purchase.CurrentRefund(settlement);
        if (current != expectedRefund) throw new InvalidOperationException("Компенсация изменилась. Откройте документ заново.");
        if (current == newRefund) throw new InvalidOperationException("Итоговая компенсация не изменилась.");
        var cost = purchase.StockValuations.LastOrDefault(x => x.Reason == StockValuationReason.Shortage && x.OperationId == settlement.Id)?.NewValue
            ?? settlement.AllocatedCost;
        if (cost is null || newRefund > cost)
            throw new InvalidOperationException("Компенсация не может превышать текущую стоимость недостачи.");
        return await EntityRollback.RunAsync(purchase, async () =>
        {
            var correction = new PurchaseCompensationCorrection(operationId, settlementId, DateTimeOffset.UtcNow, current, newRefund, reason.Trim());
            purchase.CompensationCorrections.Add(correction);
            purchase.StockValuations.Add(new(Guid.NewGuid(), operationId, correction.RecognizedAt,
                StockValuationReason.ShortageCompensation, null, settlement.PurchaseItemId, 0, 0, current, newRefund, 0, current - newRefund));
            await store.CommitAsync(InventoryCommit.Create(purchases: [purchase]), token);
            return correction;
        });
    }

    public Task CloseAsync(Purchase purchase, IReadOnlyList<PurchaseShortageInput> input, Guid operationId, CancellationToken token = default) => EntityRollback.RunAsync(purchase, async () =>
    {
        using var gate = await InventoryLock.AcquireAsync(token);
        if (operationId == Guid.Empty) throw new InvalidOperationException("Не указан идентификатор закрытия.");
        if (purchase.ClosedAt is not null)
        {
            if (purchase.ClosingOperationId == operationId) return;
            throw new InvalidOperationException("Закупка уже закрыта.");
        }
        var repository = commerce ?? throw new InvalidOperationException("Хранилище закупок недоступно.");
        var saved = await repository.GetPurchaseAsync(purchase.Id, token) ?? throw new InvalidOperationException("Сначала сохраните закупку.");
        if (JsonSerializer.Serialize(saved) != JsonSerializer.Serialize(purchase))
            throw new InvalidOperationException("Закупка изменена. Сохраните или перечитайте её перед закрытием.");
        if (purchase.Status == PurchaseStatus.Cancelled) throw new InvalidOperationException("Отменённую закупку нельзя закрыть.");
        if (!purchase.HasCompleteCostInputs) throw new InvalidOperationException("Перед закрытием укажите стоимость закупки и расходов.");
        var missing = purchase.Items.Where(x => x.MissingQuantity > 0).ToList();
        if (input.Count != missing.Count || input.Select(x => x.PurchaseItemId).Distinct().Count() != input.Count
            || input.Any(x => missing.All(item => item.Id != x.PurchaseItemId)))
            throw new InvalidOperationException("Укажите компенсацию по каждой недополученной позиции. Ноль означает потерю.");
        var now = DateTimeOffset.UtcNow;
        foreach (var item in missing)
        {
            var compensation = input.Single(x => x.PurchaseItemId == item.Id).SupplierRefund;
            var value = FifoCostCalculator.Allocate(purchase.ItemLandedCostUzs(item), item.Quantity!.Value, item.ReceivedQuantity ?? 0, item.MissingQuantity)!.Value;
            if (compensation < 0 || compensation > value || Math.Round(compensation, 2) != compensation)
                throw new InvalidOperationException($"Компенсация «{item.ProductName}» должна быть от 0 до {value:N2}.");
            var settlement = new PurchaseShortageSettlement(Guid.NewGuid(), item.Id, now, item.MissingQuantity, value, compensation, value - compensation);
            purchase.ShortageSettlements.Add(settlement);
            purchase.StockValuations.Add(new(Guid.NewGuid(), settlement.Id, now, StockValuationReason.Shortage,
                null, item.Id, 0, 0, null, value, 0, value - compensation));
        }
        purchase.ClosedAt = now;
        purchase.ClosingOperationId = operationId;
        await store.CommitAsync(InventoryCommit.Create(purchases: [purchase]), token);
    });

    private static void RecordDefect(Purchase purchase, PurchaseReceipt receipt, PurchaseReceiptLine line, decimal? value, DateTimeOffset at)
    {
        var previous = purchase.StockValuations.LastOrDefault(x => x.Reason == StockValuationReason.ReceiptDefect
            && x.OperationId == receipt.Id && x.PurchaseItemId == line.PurchaseItemId);
        if (previous is not null && previous.NewValue == value) return;
        purchase.StockValuations.Add(new(Guid.NewGuid(), receipt.Id, at, StockValuationReason.ReceiptDefect,
            null, line.PurchaseItemId, 0, 0, previous?.NewValue, value, 0, value - (previous?.NewValue ?? 0)));
    }

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
        if (purchase.ClosedAt is not null) throw new InvalidOperationException("Закупка закрыта. Допоставка требует отдельного корректирующего документа.");
        if (commerce is not null && await commerce.GetPurchaseAsync(purchase.Id, cancellationToken) is { } saved
            && JsonSerializer.Serialize(saved.Receipts) != JsonSerializer.Serialize(purchase.Receipts))
            throw new InventoryException("Приёмки закупки уже изменились. Откройте закупку заново.");
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

            var receiptValue = FifoCostCalculator.Allocate(purchase.HasCompleteCostInputs ? purchase.ItemLandedCostUzs(item) : null,
                item.Quantity!.Value, item.ReceivedQuantity!.Value - entry.ReceivedQuantity, entry.ReceivedQuantity);
            if (accepted == 0) RecordDefect(purchase, receipt, line, receiptValue, receipt.ReceivedAt);
            if (item.ProductId is null) throw new InventoryException("Выберите товар для каждой позиции приёмки.");
            var product = await LoadProductAsync(item.ProductId.Value, products, cancellationToken);
            if (product is null) throw new InventoryException("Товар приёмки не найден.");
            var variant = item.ProductVariantId is not null ? product.Variants.FirstOrDefault(x => x.Id == item.ProductVariantId) : null;
            variant ??= product.Variants.FirstOrDefault(x => x.Color.Equals(item.Color, StringComparison.OrdinalIgnoreCase) && x.Size.Equals(item.Size, StringComparison.OrdinalIgnoreCase));
            if (variant is null) { variant = new ProductVariant { Color = item.Color, Size = item.Size }; product.Variants.Add(variant); }
            StockLayerOperations.PrepareEmpty(variant);
            if (accepted > 0)
            {
                var layer = StockLayerOperations.Add(variant, accepted, receiptValue, StockLayerSource.PurchaseReceipt, receipt.ReceivedAt, purchase.Number, line.Id);
                layer.PurchaseId = purchase.Id; layer.PurchaseItemId = item.Id; layer.ReceiptId = receipt.Id; layer.ReceiptLineId = line.Id;
            }
            item.ProductVariantId = variant.Id;
            item.StockedQuantity += accepted;
            line.ProductVariantId = variant.Id;
            line.StockedQuantity = accepted;
            if (accepted > 0) ApplyProductCost(purchase, product);
            if (accepted != 0) movementRecords.Add(new StockMovement { Type = StockMovementType.PurchaseReceipt, ProductId = product.Id, ProductVariantId = variant.Id, ProductName = product.Name, Sku = product.Sku, Color = variant.Color, Size = variant.Size, QuantityDelta = accepted, SourceType = "Purchase", SourceId = purchase.Id, SourceNumber = purchase.Number, Note = $"Приёмка {receipt.ReceivedAt.ToLocalTime():dd.MM.yyyy}" });
            if (accepted > 0) StockLayerOperations.SetValue(movementRecords[^1], new(receiptValue ?? 0, receiptValue is null ? accepted : 0), 1);
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

    private static decimal? ReceiptValue(Purchase purchase, PurchaseReceipt receipt, PurchaseReceiptLine line)
    {
        var item = purchase.Items.Single(x => x.Id == line.PurchaseItemId);
        var previous = purchase.Receipts.TakeWhile(x => x.Id != receipt.Id).SelectMany(x => x.Lines)
            .Where(x => x.PurchaseItemId == item.Id).Sum(x => x.ReceivedQuantity);
        return FifoCostCalculator.Allocate(purchase.HasCompleteCostInputs ? purchase.ItemLandedCostUzs(item) : null,
            item.Quantity!.Value, previous, line.ReceivedQuantity);
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
                UnitLandedCostUzs = (ReceiptValue(purchase, receipt, line) ?? 0) / line.StockedQuantity
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
