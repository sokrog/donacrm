using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;

namespace Dona.Crm.Core.Tests;

public sealed class PurchaseLayerReportTests
{
    [Fact]
    public void Reconciles_returns_defects_writeoffs_and_revaluation_without_double_counting()
    {
        var purchase = new Purchase { CurrencyCode = "UZS", Items = [new() { Quantity = 12, ReceivedQuantity = 10, UnitPriceCny = 100 }] };
        var layer = new StockLayer { PurchaseId = purchase.Id, InitialQuantity = 10, RemainingQuantity = 3, InitialValue = 1200, RemainingValue = 360 };
        var product = new Product { Name = "Товар", Status = ProductStatus.Archived, Variants = [new() { Layers = [layer] }] };
        var consumption = new LayerConsumption(Guid.NewGuid(), layer.Id, 6, 100, 600, 0);
        var item = new SaleItem { Quantity = 6, SoldQuantity = 6, UnitPriceUzs = 200, Consumptions = [consumption] };
        var sale = new Sale { Status = SaleStatus.Completed, DiscountUzs = 60, Items = [item], Returns = [new() { RefundAmountUzs = 380, Items = [
            new() { SaleItemId = item.Id, Quantity = 1, Disposition = ReturnDisposition.Restock, LayerAllocations = [new(consumption.Id, layer.Id, 1, 100)] },
            new() { SaleItemId = item.Id, Quantity = 1, Disposition = ReturnDisposition.Defect, LayerAllocations = [new(consumption.Id, layer.Id, 1, 100)] }
        ] }] };
        var movement = new StockMovement { Type = StockMovementType.Adjustment, QuantityDelta = -2, Consumptions = [new(Guid.NewGuid(), layer.Id, 2, 100, 200, 0)] };
        purchase.StockValuations.Add(new(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.Now, StockValuationReason.Revaluation, layer.Id, null, 0, 1, 1000, 1200, 60, 140));
        var report = PurchaseLayerReport.Build(purchase, [product], [sale], [movement]);
        var row = Assert.Single(report.Layers);
        Assert.Equal(4, row.NetSold);
        Assert.Equal(1, row.DefectiveReturns);
        Assert.Equal(3, row.Remaining);
        Assert.Equal(0, row.QuantityDifference);
        Assert.Equal(0m, row.ValueDifference);
        Assert.Equal(760m, row.Revenue);
        Assert.Equal(500m, row.KnownNetCost);
        Assert.Equal(340m, row.KnownExpenses);
        Assert.Equal(-80m, row.Profit);
        Assert.Equal(200m, Assert.Single(report.Pending).Value);
        purchase.ClosedAt = DateTimeOffset.Now;
        Assert.Empty(PurchaseLayerReport.Build(purchase, [product], [sale], [movement]).Pending);
    }

    [Fact]
    public void Unknown_cost_money_only_refund_and_missing_movements_are_not_reported_as_exact()
    {
        var purchase = new Purchase();
        var layer = new StockLayer { PurchaseId = purchase.Id, InitialQuantity = 3, RemainingQuantity = 1 };
        var product = new Product { Variants = [new() { Layers = [layer] }] };
        var sale = new Sale { Status = SaleStatus.Completed, Items = [new() { Quantity = 1, SoldQuantity = 1, UnitPriceUzs = 100,
            Consumptions = [new(Guid.NewGuid(), layer.Id, 1, null, null, 0)] }], Returns = [new() { RefundAmountUzs = 20 }] };
        var row = Assert.Single(PurchaseLayerReport.Build(purchase, [product], [sale], []).Layers);
        Assert.True(row.UnknownCost);
        Assert.True(row.ApproximateRevenue);
        Assert.Null(row.Profit);
        Assert.Null(row.ValueDifference);
        Assert.Equal(-1, row.QuantityDifference);
    }

    [Fact]
    public void Full_defect_and_shortage_losses_remain_separate_from_physical_layers()
    {
        var purchase = new Purchase { ClosedAt = DateTimeOffset.Now };
        purchase.StockValuations.Add(new(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.Now, StockValuationReason.ReceiptDefect, null, null, 0, 1, null, 1000, 0, 1000));
        purchase.StockValuations.Add(new(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.Now, StockValuationReason.Shortage, null, null, 0, 1, null, 200, 0, 80));
        var report = PurchaseLayerReport.Build(purchase, [], [], []);
        Assert.Empty(report.Layers);
        Assert.Empty(report.Pending);
        Assert.Equal(1080m, report.KnownUnlayeredLoss);
    }
}
