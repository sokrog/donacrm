using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;

namespace Dona.Crm.Core.Tests;

public sealed class PeriodAccountingTests
{
    private static readonly DateTimeOffset September = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset October = September.AddMonths(1);

    [Fact]
    public void Recent_receipt_does_not_hide_an_old_remaining_layer()
    {
        var old = new StockLayer { InitialQuantity = 2, RemainingQuantity = 2, InitialValue = 200, RemainingValue = 200, ReceivedAt = September.AddMonths(-3) };
        var fresh = new StockLayer { InitialQuantity = 5, RemainingQuantity = 5, InitialValue = 1000, RemainingValue = 1000, ReceivedAt = October.AddDays(-1) };
        var product = new Product { CreatedAt = October, Variants = [new() { Quantity = 7, StockLayerVersion = 1, Layers = [old, fresh] }] };
        var report = new InventoryAnalyticsService().Build([product], [], [], new() { StaleInventoryDays = 60 }, October);
        Assert.Equal(200m, report.StaleInventoryCostUzs);
        Assert.True(Assert.Single(report.Rows).IsStale);
        Assert.True(report.Rows[0].OldestLayerAgeDays > 60);
    }

    [Fact]
    public void Later_return_and_expense_leave_closed_month_unchanged()
    {
        var layerId = Guid.NewGuid();
        var item = new SaleItem { Quantity = 2, SoldQuantity = 2, UnitPriceUzs = 250,
            Consumptions = [new(Guid.NewGuid(), layerId, 2, 100, 200, 0)] };
        var sale = new Sale { Status = SaleStatus.Completed, CreatedAt = September.AddMonths(-1), CompletedAt = September, Items = [item] };
        var service = new AnalyticsService();
        var before = service.Build([sale], [], new(2026, 9, 1), new(2026, 9, 30));
        sale.Returns.Add(new() { CreatedAt = October, RefundAmountUzs = 250,
            Items = [new() { SaleItemId = item.Id, Quantity = 1, Disposition = ReturnDisposition.Restock,
                LayerAllocations = [new(item.Consumptions[0].Id, layerId, 1, 100)] }] });
        item.ReturnedQuantity = 1;
        var purchase = new Purchase { Number = "P", StockValuations = [new(Guid.NewGuid(), Guid.NewGuid(), October,
            StockValuationReason.Revaluation, layerId, null, 0, 1, 200, 240, 0, 40)] };
        var after = service.Build([sale], [], new(2026, 9, 1), new(2026, 9, 30), [purchase]);
        Assert.Equal(500m, after.RevenueUzs);
        Assert.Equal(200m, after.CostUzs);
        Assert.Equal(before.ProfitUzs, after.ProfitUzs);
        var current = service.Build([sale], [], new(2026, 10, 1), new(2026, 10, 31), [purchase]);
        Assert.Equal(0, current.Orders);
        Assert.Equal(-250m, current.RevenueUzs);
        Assert.Equal(-100m, current.CostUzs);
        Assert.Equal(40m, current.PeriodExpensesUzs);
        Assert.Equal(-190m, current.ProfitUzs);
        Assert.Equal(October.LocalDateTime.Date, Assert.Single(current.Daily).Date);
    }

    [Fact]
    public void Damaged_customer_return_keeps_original_cost_without_charging_it_twice()
    {
        var item = new SaleItem { Quantity = 1, SoldQuantity = 1, UnitPriceUzs = 250, UnitCostUzs = 100 };
        var sale = new Sale { Status = SaleStatus.Returned, CompletedAt = September, Items = [item],
            Returns = [new() { CreatedAt = October, RefundAmountUzs = 250,
                Items = [new() { SaleItemId = item.Id, Quantity = 1, Disposition = ReturnDisposition.Defect }] }] };
        var report = new AnalyticsService().Build([sale], [], new(2026, 10, 1), new(2026, 10, 31));
        Assert.Equal(-250m, report.ProfitUzs);
        Assert.Equal(0m, report.CostUzs);
        Assert.Equal(0m, report.PeriodExpensesUzs);
    }

    [Fact]
    public void Physical_writeoff_and_unknown_loss_are_separate_period_expenses()
    {
        var movements = new[]
        {
            new StockMovement { Type = StockMovementType.Adjustment, CreatedAt = October, QuantityDelta = -2, ValueDelta = -200 },
            new StockMovement { Type = StockMovementType.Adjustment, CreatedAt = October, QuantityDelta = -1, ValueDelta = null },
            new StockMovement { Type = StockMovementType.Sale, CreatedAt = October, QuantityDelta = -1, ValueDelta = -100 }
        };
        var report = new AnalyticsService().Build([], [], null, null, movements: movements);
        Assert.Equal(2, report.Expenses.Count);
        Assert.Equal(200m, report.PeriodExpensesUzs);
        Assert.True(report.HasUnknownCost);
    }

    [Fact]
    public void Supplier_profit_uses_consumed_layers_and_return_date()
    {
        var first = new Purchase { SupplierName = "Первый" };
        var second = new Purchase { SupplierName = "Второй" };
        var a = new StockLayer { PurchaseId = first.Id };
        var b = new StockLayer { PurchaseId = second.Id };
        var product = new Product { Variants = [new() { Layers = [a, b] }] };
        var item = new SaleItem { ProductId = product.Id, Quantity = 2, SoldQuantity = 2, UnitPriceUzs = 250,
            Consumptions = [new(Guid.NewGuid(), a.Id, 1, 100, 100, 0), new(Guid.NewGuid(), b.Id, 1, 150, 150, 0)] };
        var sale = new Sale { Status = SaleStatus.Completed, CompletedAt = September, Items = [item],
            Returns = [new() { CreatedAt = October, RefundAmountUzs = 250, Items = [new() { SaleItemId = item.Id,
                Quantity = 1, Disposition = ReturnDisposition.Restock, LayerAllocations = [new(item.Consumptions[1].Id, b.Id, 1, 150)] }] }] };
        var service = new ProfitAnalyticsService();
        var past = service.Build([sale], [product], [first, second], new(), new(2026, 9, 1), new(2026, 9, 30));
        Assert.Equal(150m, past.Suppliers.Single(x => x.Name == "Первый").ProfitUzs);
        Assert.Equal(100m, past.Suppliers.Single(x => x.Name == "Второй").ProfitUzs);
        var current = service.Build([sale], [product], [first, second], new(), new(2026, 10, 1), new(2026, 10, 31));
        var returned = Assert.Single(current.Suppliers);
        Assert.Equal("Второй", returned.Name);
        Assert.Equal(-100m, returned.ProfitUzs);
        Assert.Equal(0, returned.Orders);
    }
}
