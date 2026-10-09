using System.Text.Json;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;

namespace Dona.Crm.Core.Tests;

public sealed class PurchaseLossTests
{
    [Theory]
    [InlineData(1, 9)]
    [InlineData(3, 7)]
    public async Task Personal_use_withdraws_explicit_count_not_stale_target_balance(int count, int expected)
    {
        var s = new Scenario(10, 0); await s.Start();
        var movement = await new StockAdjustmentService(s.Catalog, s.Store).AdjustAsync(new()
        {
            ProductId = s.Product.Id, ProductVariantId = s.Product.Variants[0].Id,
            WithdrawQuantity = count, NewQuantity = 0, Reason = StockAdjustmentReason.PersonalUse, Note = "Для себя"
        });
        Assert.Equal(-count, movement.QuantityDelta);
        Assert.Equal(expected, (await s.Layer()).RemainingQuantity);
    }

    [Fact]
    public async Task Backdated_order_keeps_actual_order_date_when_saved()
    {
        var s = new Scenario(); var service = s.Service();
        s.Purchase.OrderedAt = new DateTimeOffset(2025, 4, 12, 0, 0, 0, TimeSpan.FromHours(5));
        await service.SaveAsync(s.Purchase);
        Assert.Equal(s.Purchase.OrderedAt, Assert.Single(s.Store.Commits.Last().Purchases).OrderedAt);
        Assert.Equal(2025, s.Purchase.OrderedAt.Year);
    }
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private sealed class Scenario
    {
        public Purchase Purchase = new() { Number = "LOSS", CurrencyCode = "UZS", RateToUzs = 1, Status = PurchaseStatus.Ordered };
        public Product Product = new() { Name = "Товар", Sku = "LOSS", Variants = [new() { Color = "Белый", Size = "S", Quantity = 0 }] };
        public CloningCatalog Catalog;
        public MemoryInventoryStore Store;
        public Scenario(int received = 8, int defects = 2)
        {
            Purchase.Items = [new() { ProductId = Product.Id, ProductVariantId = Product.Variants[0].Id, ProductName = Product.Name, Quantity = 10, UnitPrice = 100 }];
            Catalog = new(Product); Store = new(Catalog);
            Receive = received; Defects = defects;
        }
        public int Receive, Defects;
        public PurchaseReceivingService Service() => new(Catalog, Store, new ProductEditingServiceTests.StubCommerce(Copy(Purchase)));
        public async Task Start() => await Service().ReceiveAsync(Purchase, [new(Purchase.Items[0].Id, Receive, Defects)]);
        public async Task<StockLayer> Layer() => Assert.Single((await Catalog.GetProductAsync(Product.Id))!.Variants[0].Layers);
        public List<PurchaseShortageInput> Shortages(decimal refund = 0) => Purchase.ReceivingCompletedAt is not null || Purchase.Items[0].MissingQuantity == 0 ? [] : [new(Purchase.Items[0].Id, refund)];
        public List<PurchaseLossInput> Choices(LossTreatment defect, LossTreatment shortage) => Enum.GetValues<PurchaseLossKind>()
            .Where(k => PurchaseReceivingService.PendingLoss(Purchase, Purchase.Items[0], k) > 0)
            .Select(k => new PurchaseLossInput(Purchase.Items[0].Id, k, k == PurchaseLossKind.Defect ? defect : shortage)).ToList();
    }

    [Theory]
    [InlineData(LossTreatment.PeriodExpense, LossTreatment.PeriodExpense, 600, 400)]
    [InlineData(LossTreatment.RemainingStock, LossTreatment.PeriodExpense, 800, 200)]
    [InlineData(LossTreatment.PeriodExpense, LossTreatment.RemainingStock, 800, 200)]
    [InlineData(LossTreatment.RemainingStock, LossTreatment.RemainingStock, 1000, 0)]
    public async Task Explicit_decisions_reconcile_stock_and_expenses(LossTreatment defect, LossTreatment shortage, decimal stock, decimal expense)
    {
        var s = new Scenario(); await s.Start();
        Assert.Equal(800, (await s.Layer()).RemainingValue); // Legacy partial-defect handling stays intact until reviewed.
        await s.Service().ResolveLossesAsync(s.Purchase, s.Shortages(), s.Choices(defect, shortage), Guid.NewGuid());
        Assert.Equal(stock, (await s.Layer()).RemainingValue);
        Assert.Equal(expense, s.Purchase.StockValuations.Sum(x => x.ExpenseDelta));
        Assert.Equal(1000, stock + expense);
        Assert.NotNull(s.Purchase.ReceivingCompletedAt);
        Assert.Null(s.Purchase.ClosedAt);
        var report = PurchaseLayerReport.Build(s.Purchase, await s.Catalog.GetProductsAsync(), [], []);
        Assert.Equal(0, Assert.Single(report.Layers).ValueDifference);
    }

    [Fact]
    public async Task Already_issued_fifo_cost_is_preserved_and_loss_goes_only_to_remaining_stock()
    {
        var s = new Scenario(); await s.Start();
        var product = (await s.Catalog.GetProductAsync(s.Product.Id))!;
        var issued = FifoCostCalculator.Consume(product.Variants[0], 3);
        s.Catalog.Put(product);
        await s.Service().ResolveLossesAsync(s.Purchase, s.Shortages(), s.Choices(LossTreatment.RemainingStock, LossTreatment.RemainingStock), Guid.NewGuid());
        Assert.Equal(400, FifoCostCalculator.Cost(issued).TotalValue);
        Assert.Equal(700, (await s.Layer()).RemainingValue);
        Assert.Equal(-100, s.Purchase.StockValuations.Sum(x => x.ExpenseDelta));
        Assert.Equal(1000, 400 + 700 + s.Purchase.StockValuations.Sum(x => x.ExpenseDelta));
    }

    [Fact]
    public async Task Late_costs_preserve_capitalized_amount_and_are_not_capitalized_twice()
    {
        var s = new Scenario(); await s.Start();
        await s.Service().ResolveLossesAsync(s.Purchase, s.Shortages(), s.Choices(LossTreatment.RemainingStock, LossTreatment.RemainingStock), Guid.NewGuid());
        var service = s.Service();
        s.Purchase.Expenses.Add(new() { Name = "Поздняя доставка", Amount = 100, CurrencyCode = "UZS" });
        await service.SaveAsync(s.Purchase);
        Assert.Equal(1060, (await s.Layer()).RemainingValue);
        Assert.Equal(40, s.Purchase.StockValuations.Sum(x => x.ExpenseDelta));
        Assert.Equal(40, s.Purchase.Items.Sum(i => Enum.GetValues<PurchaseLossKind>().Sum(k => PurchaseReceivingService.PendingLoss(s.Purchase, i, k))));
        await s.Service().ResolveLossesAsync(s.Purchase, [], s.Choices(LossTreatment.RemainingStock, LossTreatment.RemainingStock), Guid.NewGuid());
        Assert.Equal(1100, (await s.Layer()).RemainingValue);
        Assert.Equal(0, s.Purchase.StockValuations.Sum(x => x.ExpenseDelta));
    }

    [Fact]
    public async Task Refund_reduces_shortage_before_allocation_and_final_close_does_not_duplicate_it()
    {
        var s = new Scenario(); await s.Start();
        await s.Service().ResolveLossesAsync(s.Purchase, s.Shortages(100), s.Choices(LossTreatment.PeriodExpense, LossTreatment.RemainingStock), Guid.NewGuid());
        Assert.Equal(700, (await s.Layer()).RemainingValue);
        Assert.Equal(200, s.Purchase.StockValuations.Sum(x => x.ExpenseDelta));
        await s.Service().CloseAsync(s.Purchase, [], Guid.NewGuid());
        Assert.Single(s.Purchase.ShortageSettlements);
        Assert.NotNull(s.Purchase.ClosedAt);
    }

    [Fact]
    public async Task Failure_to_allocate_all_defective_receipt_rolls_back_entire_document()
    {
        var s = new Scenario(10, 10); await s.Start(); var before = JsonSerializer.Serialize(s.Purchase);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service().ResolveLossesAsync(s.Purchase, [], s.Choices(LossTreatment.RemainingStock, LossTreatment.PeriodExpense), Guid.NewGuid()));
        Assert.Equal(before, JsonSerializer.Serialize(s.Purchase));
        Assert.Empty((await s.Catalog.GetProductAsync(s.Product.Id))!.Variants[0].Layers);
    }

    [Fact]
    public async Task Store_failure_leaves_purchase_and_stock_unchanged()
    {
        var s = new Scenario(); await s.Start(); var before = JsonSerializer.Serialize(s.Purchase);
        s.Store.FailWith = new IOException("disk");
        await Assert.ThrowsAsync<IOException>(() => s.Service().ResolveLossesAsync(s.Purchase, s.Shortages(), s.Choices(LossTreatment.RemainingStock, LossTreatment.RemainingStock), Guid.NewGuid()));
        Assert.Equal(before, JsonSerializer.Serialize(s.Purchase));
        Assert.Equal(800, (await s.Layer()).RemainingValue);
    }

    [Fact]
    public async Task Replayed_document_is_idempotent_and_receiving_cannot_restart()
    {
        var s = new Scenario(); await s.Start(); var id = Guid.NewGuid(); var choices = s.Choices(LossTreatment.PeriodExpense, LossTreatment.PeriodExpense); var shortages = s.Shortages();
        await s.Service().ResolveLossesAsync(s.Purchase, shortages, choices, id);
        var count = s.Store.Commits.Count;
        await s.Service().ResolveLossesAsync(s.Purchase, shortages, choices, id);
        Assert.Equal(count, s.Store.Commits.Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service().ReceiveAsync(s.Purchase, [new(s.Purchase.Items[0].Id, 1, 0)]));
    }

    [Theory]
    [InlineData(LossTreatment.PeriodExpense, 800, 0)]
    [InlineData(LossTreatment.RemainingStock, 1000, -200)]
    public async Task Personal_use_has_its_own_reason_and_selected_treatment(LossTreatment treatment, decimal stock, decimal transfer)
    {
        var s = new Scenario(10, 0); await s.Start();
        var service = new StockAdjustmentService(s.Catalog, s.Store);
        var movement = await service.AdjustAsync(new() { ProductId = s.Product.Id, ProductVariantId = s.Product.Variants[0].Id, NewQuantity = 8, Reason = StockAdjustmentReason.PersonalUse, Note = "Для себя", LossTreatment = treatment });
        var product = (await s.Catalog.GetProductAsync(s.Product.Id))!;
        Assert.Equal("Личное изъятие", movement.SourceNumber);
        Assert.Equal(stock, FifoCostCalculator.Value(product.Variants[0]).TotalValue);
        Assert.Equal(transfer, product.StockValuations.Sum(x => x.ExpenseDelta));
        var report = new AnalyticsService().Build([], [product], null, null, [s.Purchase], [movement]);
        Assert.Equal(200 + transfer, report.PeriodExpensesUzs);
        Assert.Equal(0, report.RevenueUzs);
    }

    [Fact]
    public async Task Personal_use_cannot_increase_quantity_or_capitalize_without_remaining_stock()
    {
        var s = new Scenario(10, 0); await s.Start(); var service = new StockAdjustmentService(s.Catalog, s.Store);
        var request = new StockAdjustmentRequest { ProductId = s.Product.Id, ProductVariantId = s.Product.Variants[0].Id, NewQuantity = 11, Reason = StockAdjustmentReason.PersonalUse, Note = "Для себя" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AdjustAsync(request));
        request.NewQuantity = 0; request.LossTreatment = LossTreatment.RemainingStock;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AdjustAsync(request));
        Assert.Equal(10, (await s.Layer()).RemainingQuantity);
    }

    [Fact]
    public async Task Late_delivery_before_final_close_reconciles_with_previous_capitalization()
    {
        var s = new Scenario(); await s.Start();
        await s.Service().ResolveLossesAsync(s.Purchase, s.Shortages(), s.Choices(LossTreatment.RemainingStock, LossTreatment.RemainingStock), Guid.NewGuid());
        var id = Assert.Single(s.Purchase.ShortageSettlements).Id;
        await s.Service().ReceiveLateAsync(s.Purchase.Id, id, Guid.NewGuid(), 2, 2, 1, "Поздняя поставка");
        var updated = Assert.Single(s.Store.Commits.Last().Purchases);
        var product = (await s.Catalog.GetProductAsync(s.Product.Id))!;
        Assert.Equal(7, product.Variants[0].Quantity);
        Assert.Equal(1100, FifoCostCalculator.Value(product.Variants[0]).TotalValue);
        Assert.Equal(-100, updated.StockValuations.Sum(x => x.ExpenseDelta));
        Assert.Equal(1000, FifoCostCalculator.Value(product.Variants[0]).TotalValue + updated.StockValuations.Sum(x => x.ExpenseDelta));
        Assert.Equal(100, PurchaseReceivingService.PendingLoss(updated, updated.Items[0], PurchaseLossKind.Defect));
    }

    [Fact]
    public async Task Later_refund_is_a_separate_recovery_and_does_not_duplicate_inventory_value()
    {
        var s = new Scenario(); await s.Start();
        await s.Service().ResolveLossesAsync(s.Purchase, s.Shortages(), s.Choices(LossTreatment.RemainingStock, LossTreatment.RemainingStock), Guid.NewGuid());
        var settlement = Assert.Single(s.Purchase.ShortageSettlements);
        await s.Service().CorrectCompensationAsync(s.Purchase.Id, settlement.Id, Guid.NewGuid(), 0, 100, "Поставщик вернул деньги");
        var updated = Assert.Single(s.Store.Commits.Last().Purchases);
        Assert.Equal(-100, updated.StockValuations.Sum(x => x.ExpenseDelta));
        Assert.Equal(1000, (await s.Layer()).RemainingValue);
    }

    [Fact]
    public async Task Cost_change_after_personal_use_does_not_erase_transferred_cost()
    {
        var s = new Scenario(10, 0); await s.Start();
        await new StockAdjustmentService(s.Catalog, s.Store).AdjustAsync(new() { ProductId = s.Product.Id, ProductVariantId = s.Product.Variants[0].Id, NewQuantity = 8, Reason = StockAdjustmentReason.PersonalUse, Note = "Для себя", LossTreatment = LossTreatment.RemainingStock });
        var service = s.Service(); s.Purchase.Items[0].UnitPrice = 110;
        await service.SaveAsync(s.Purchase);
        Assert.Equal(1080, (await s.Layer()).RemainingValue);
        Assert.Equal(200, (await s.Layer()).CapitalizedLossValue);
    }

    [Fact]
    public async Task Backup_roundtrip_preserves_decisions_and_capitalized_stock()
    {
        var s = new Scenario(); await s.Start();
        await s.Service().ResolveLossesAsync(s.Purchase, s.Shortages(), s.Choices(LossTreatment.RemainingStock, LossTreatment.RemainingStock), Guid.NewGuid());
        var snapshot = new DonaSyncSnapshot { Products = (await s.Catalog.GetProductsAsync()).ToList(), Purchases = [s.Purchase] };
        var archive = BackupArchiveCodec.Create(BackupSnapshotMapper.FromSyncSnapshot(snapshot));
        var restored = BackupSnapshotMapper.ToSyncSnapshot(BackupArchiveCodec.Inspect(archive).Snapshot);
        Assert.Equal(DonaSyncFingerprint.Create(snapshot), DonaSyncFingerprint.Create(restored));
        Assert.Equal(400, restored.Products[0].Variants[0].Layers[0].CapitalizedLossValue);
        Assert.Equal(2, restored.Purchases[0].LossDecisions.Count);
    }
}
