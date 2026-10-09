using System.Text.Json;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Web.Services;

public sealed partial class PurchaseReceivingService
{
    public static bool HasUnreviewedLosses(Purchase purchase) => purchase.HasCompleteCostInputs
        && purchase.Items.Any(i => Enum.GetValues<PurchaseLossKind>().Any(k => PendingLoss(purchase, i, k) > 0));
    public static decimal DefectCost(Purchase purchase, PurchaseItem item)
    {
        decimal result = 0;
        var previous = 0;
        foreach (var line in purchase.Receipts.SelectMany(x => x.Lines).Where(x => x.PurchaseItemId == item.Id))
        {
            var gross = FifoCostCalculator.Allocate(purchase.ItemLandedCostUzs(item), item.Quantity!.Value, previous, line.ReceivedQuantity)!.Value;
            result += gross - FifoCostCalculator.Allocate(gross, line.ReceivedQuantity, 0, line.StockedQuantity)!.Value;
            previous += line.ReceivedQuantity;
        }
        return result;
    }

    public static decimal ShortageCost(Purchase purchase, PurchaseItem item) =>
        FifoCostCalculator.Allocate(purchase.ItemLandedCostUzs(item), item.Quantity!.Value,
            item.ReceivedQuantity ?? 0, item.MissingQuantity)!.Value;

    public static decimal PendingLoss(Purchase purchase, PurchaseItem item, PurchaseLossKind kind, decimal refund = 0)
    {
        var total = kind == PurchaseLossKind.Defect ? DefectCost(purchase, item)
            : ShortageCost(purchase, item) - (purchase.ShortageSettlements.FirstOrDefault(x => x.PurchaseItemId == item.Id) is { } settlement
                ? purchase.CurrentRefund(settlement) : refund);
        return Math.Max(0, total - purchase.LossDecisions.Where(x => x.PurchaseItemId == item.Id && x.Kind == kind).Sum(x => x.Amount));
    }

    public Task ResolveLossesAsync(Purchase purchase, IReadOnlyList<PurchaseShortageInput> shortages,
        IReadOnlyList<PurchaseLossInput> choices, Guid operationId, CancellationToken token = default) => EntityRollback.RunAsync(purchase, async () =>
    {
        using var gate = await InventoryLock.AcquireAsync(token);
        if (operationId == Guid.Empty) throw new InvalidOperationException("Не указан документ разбора расхождений.");
        var signature = JsonSerializer.Serialize(new { shortages, choices });
        var repository = commerce ?? throw new InvalidOperationException("Хранилище закупок недоступно.");
        var saved = await repository.GetPurchaseAsync(purchase.Id, token) ?? throw new InvalidOperationException("Сначала сохраните закупку.");
        if (saved.LossReviews.FirstOrDefault(x => x.OperationId == operationId) is { } replay)
        {
            if (replay.Signature != signature) throw new InvalidOperationException("Документ уже сохранён с другим выбором.");
            return;
        }
        if (JsonSerializer.Serialize(saved) != JsonSerializer.Serialize(purchase))
            throw new InvalidOperationException("Закупка изменилась. Откройте этап заново.");
        if (purchase.Status == PurchaseStatus.Cancelled || !purchase.HasCompleteCostInputs)
            throw new InvalidOperationException("Нужна действующая закупка с заполненными ценами и расходами.");
        if (choices.Any(x => !Enum.IsDefined(x.Kind) || !Enum.IsDefined(x.Treatment))
            || choices.Select(x => (x.PurchaseItemId, x.Kind)).Distinct().Count() != choices.Count)
            throw new InvalidOperationException("Выберите способ учёта каждой потери без повторов.");
        var now = DateTimeOffset.UtcNow;
        if (purchase.ReceivingCompletedAt is null && purchase.ClosedAt is null)
        {
            var missing = purchase.Items.Where(x => x.MissingQuantity > 0).ToList();
            if (shortages.Count != missing.Count || shortages.Select(x => x.PurchaseItemId).Distinct().Count() != shortages.Count
                || shortages.Any(x => missing.All(i => i.Id != x.PurchaseItemId)))
                throw new InvalidOperationException("Укажите компенсацию по каждой недостаче.");
            foreach (var item in missing)
            {
                var refund = shortages.Single(x => x.PurchaseItemId == item.Id).SupplierRefund;
                var value = ShortageCost(purchase, item);
                if (refund < 0 || refund > value || Math.Round(refund, 2) != refund)
                    throw new InvalidOperationException($"Компенсация «{item.ProductName}»: от 0 до {value:N2} UZS.");
                var settlement = new PurchaseShortageSettlement(Guid.NewGuid(), item.Id, now, item.MissingQuantity, value, refund, value - refund);
                purchase.ShortageSettlements.Add(settlement);
                purchase.StockValuations.Add(new(Guid.NewGuid(), settlement.Id, now, StockValuationReason.Shortage,
                    null, item.Id, 0, 0, null, value, 0, value - refund));
            }
        }
        else if (shortages.Count != 0) throw new InvalidOperationException("Компенсация принятой недостачи изменяется отдельным документом.");
        var pending = purchase.Items.SelectMany(item => Enum.GetValues<PurchaseLossKind>().Select(kind =>
            (Item: item, Kind: kind, Amount: PendingLoss(purchase, item, kind)))).Where(x => x.Amount > 0).ToList();
        if (choices.Count != pending.Count || choices.Any(x => !pending.Any(p => p.Item.Id == x.PurchaseItemId && p.Kind == x.Kind)))
            throw new InvalidOperationException("Выберите способ учёта каждой потери. Суммы могли измениться — обновите этап.");

        var products = new Dictionary<Guid, Product>();
        foreach (var id in purchase.Items.Where(x => x.ProductId is not null).Select(x => x.ProductId!.Value).Distinct())
            await LoadProductAsync(id, products, token);
        purchase.SeparateReceiptDefects = true;
        var corrections = await RevalueReceiptsAsync(purchase, products, token);
        var targets = products.Values.SelectMany(x => x.Variants).SelectMany(x => x.Layers).Where(x => x.PurchaseId == purchase.Id).ToList();
        foreach (var loss in pending)
        {
            var treatment = choices.Single(x => x.PurchaseItemId == loss.Item.Id && x.Kind == loss.Kind).Treatment;
            if (treatment == LossTreatment.RemainingStock)
                LossCapitalization.Apply(targets, loss.Amount, operationId, loss.Item.Id, purchase.StockValuations);
            purchase.LossDecisions.Add(new(operationId, loss.Item.Id, loss.Kind, treatment, loss.Amount, now));
        }
        purchase.ReceivingCompletedAt ??= now;
        purchase.LossReviews.Add(new(operationId, signature, now));
        await store.CommitAsync(InventoryCommit.Create(products: products.Values, purchases: [purchase]) with { CostCorrections = corrections }, token);
    });

    private async Task<List<ProductCostHistoryEntry>> RevalueReceiptsAsync(Purchase purchase, Dictionary<Guid, Product> products, CancellationToken token)
    {
        var corrections = new List<ProductCostHistoryEntry>();
        foreach (var receipt in purchase.Receipts)
        {
            var (costs, _) = await BuildHistoryAsync(purchase, receipt, products, token);
            corrections.AddRange(costs);
            foreach (var line in receipt.Lines.Where(x => x.DefectQuantity > 0 && (purchase.SeparateReceiptDefects || x.StockedQuantity == 0)))
            {
                var item = purchase.Items.Single(x => x.Id == line.PurchaseItemId);
                var previous = purchase.Receipts.TakeWhile(x => x.Id != receipt.Id).SelectMany(x => x.Lines)
                    .Where(x => x.PurchaseItemId == item.Id).Sum(x => x.ReceivedQuantity);
                var gross = FifoCostCalculator.Allocate(purchase.ItemLandedCostUzs(item), item.Quantity!.Value, previous, line.ReceivedQuantity);
                var defect = purchase.SeparateReceiptDefects ? gross - ReceiptValue(purchase, receipt, line) : gross;
                RecordDefect(purchase, receipt, line, defect, DateTimeOffset.UtcNow);
            }
        }
        foreach (var layer in products.Values.SelectMany(x => x.Variants).SelectMany(x => x.Layers).Where(x => x.PurchaseId == purchase.Id))
        {
            var receipt = purchase.Receipts.Single(x => x.Id == layer.ReceiptId);
            var line = receipt.Lines.Single(x => x.Id == layer.ReceiptLineId);
            var value = ReceiptValue(purchase, receipt, line) + layer.CapitalizedLossValue;
            if (value == layer.InitialValue) continue;
            var delta = value - (layer.InitialValue ?? 0);
            var inventoryDelta = FifoCostCalculator.Allocate(value - layer.CapitalizedLossValue, layer.InitialQuantity, 0, layer.RemainingQuantity)
                - FifoCostCalculator.Allocate((layer.InitialValue ?? 0) - layer.CapitalizedLossValue, layer.InitialQuantity, 0, layer.RemainingQuantity);
            purchase.StockValuations.Add(new(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
                layer.InitialValue is null ? StockValuationReason.InitialValuation : StockValuationReason.Revaluation,
                layer.Id, layer.PurchaseItemId, layer.ValuationRevision, layer.ValuationRevision + 1,
                layer.InitialValue, value, inventoryDelta, delta - inventoryDelta));
            layer.InitialValue = value;
            layer.RemainingValue = (layer.RemainingValue ?? 0) + inventoryDelta;
            layer.ValuationRevision++;
        }
        return corrections;
    }
}
