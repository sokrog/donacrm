using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;

namespace Dona.Crm.Core.Tests;

public sealed class FifoOperationTests
{
    [Fact]
    public async Task Reserve_cancel_and_duplicate_variant_lines_do_not_change_layers()
    {
        var variant = new ProductVariant { Quantity = 0 };
        StockLayerOperations.Add(variant, 5, 500, StockLayerSource.OpeningBalance, DateTimeOffset.Now, "Начальный остаток");
        var product = new Product { Variants = [variant] };
        var catalog = new CloningCatalog(product);
        var store = new MemoryInventoryStore(catalog);
        var service = new SalesInventoryService(catalog, store);
        SaleItem Line() => new() { ProductId = product.Id, ProductVariantId = variant.Id, Quantity = 2, UnitPriceUzs = 200 };
        var sale = new Sale { Items = [Line()] };
        await service.ReserveAsync(sale);
        await service.ReserveAsync(sale);
        await service.CancelAsync(sale);
        await service.CancelAsync(sale);
        var duplicate = new Sale { Items = [Line(), Line()] };
        await Assert.ThrowsAsync<InventoryException>(() => service.ReserveAsync(duplicate));
        var actual = (await catalog.GetProductAsync(product.Id))!.Variants[0];
        Assert.Equal(5, actual.Quantity);
        Assert.Equal(0, actual.ReservedQuantity);
        Assert.Equal(variant.Layers[0].Id, Assert.Single(actual.Layers).Id);
        Assert.Equal(500m, actual.Layers[0].RemainingValue);
    }

    [Fact]
    public async Task Rejected_defective_partial_and_full_returns_never_claim_a_unit_twice()
    {
        var variant = new ProductVariant { Quantity = 0 };
        StockLayerOperations.Add(variant, 5, 500, StockLayerSource.OpeningBalance, DateTimeOffset.Now, "Начальный остаток");
        var product = new Product { Variants = [variant] };
        var catalog = new CloningCatalog(product);
        var store = new MemoryInventoryStore(catalog);
        var selling = new SalesInventoryService(catalog, store);
        var returns = new SalesReturnService(catalog, store);
        var sale = new Sale { Items = [new() { ProductId = product.Id, ProductVariantId = variant.Id, Quantity = 5, UnitPriceUzs = 200 }] };
        await selling.ReserveAsync(sale);
        await selling.MarkShippedAsync(sale);
        await selling.CompleteAsync(sale);
        SaleReturn Return(ReturnDisposition disposition) => new() { Reason = "Проверка", RefundAmountUzs = disposition == ReturnDisposition.Rejected ? 0 : 200,
            Items = [new() { SaleItemId = sale.Items[0].Id, Quantity = 1, Disposition = disposition }] };
        var rejected = await returns.CreateAsync(sale, Return(ReturnDisposition.Rejected));
        Assert.Empty(rejected.Items[0].LayerAllocations);
        Assert.Equal(0, sale.Items[0].ReturnedQuantity);
        var defect = await returns.CreateAsync(sale, Return(ReturnDisposition.Defect));
        await returns.CreateAsync(sale, defect);
        await returns.CreateAsync(sale, Return(ReturnDisposition.Restock));
        await selling.ReturnAsync(sale);
        await selling.ReturnAsync(sale);
        Assert.Equal(5, sale.Items[0].ReturnedQuantity);
        Assert.Equal(4, sale.RestockedQuantity(sale.Items[0].Id));
        Assert.Equal(100m, sale.CostUzs);
        Assert.Equal(1000m, sale.RefundedUzs);
        var actual = (await catalog.GetProductAsync(product.Id))!.Variants[0];
        Assert.Equal(4, actual.Quantity);
        Assert.Equal(400m, FifoCostCalculator.Value(actual).TotalValue);
        Assert.Equal(5, sale.Returns.SelectMany(x => x.Items).SelectMany(x => x.LayerAllocations).Sum(x => x.Quantity));
    }

    [Fact]
    public async Task Two_receipts_sale_partial_return_and_resale_reconcile_stock_and_cost()
    {
        var variant = new ProductVariant { Color = "Чёрный", Size = "M", Quantity = 0 };
        var product = new Product { Name = "Футболка", Sku = "FIFO", Variants = [variant] };
        var catalog = new CloningCatalog(product);
        var store = new MemoryInventoryStore(catalog);
        var receiving = new PurchaseReceivingService(catalog, store);
        Purchase Purchase(int quantity, decimal cost) => new()
        {
            Number = $"P-{cost}", CurrencyCode = "UZS",
            Items = [new() { ProductId = product.Id, ProductVariantId = variant.Id, ProductName = product.Name,
                Quantity = quantity, UnitPrice = cost }]
        };
        var first = Purchase(3, 100);
        var second = Purchase(10, 150);
        await receiving.ReceiveAsync(first, [new(first.Items[0].Id, 3, 0)], receivedAt: DateTimeOffset.UtcNow.AddDays(-2));
        await receiving.ReceiveAsync(second, [new(second.Items[0].Id, 10, 0)], receivedAt: DateTimeOffset.UtcNow.AddDays(-1));
        var sale = new Sale { Number = "S-1", Items = [new() { ProductId = product.Id, ProductVariantId = variant.Id, ProductName = product.Name, Quantity = 5, UnitPriceUzs = 250 }] };
        var inventory = new SalesInventoryService(catalog, store);
        await inventory.ReserveAsync(sale);
        Assert.Equal(600m, (await inventory.PreviewAsync(sale))[sale.Items[0].Id].TotalValue);
        await inventory.MarkShippedAsync(sale);
        await inventory.CompleteAsync(sale);
        Assert.Equal(600m, sale.CostUzs);
        Assert.Equal(120m, sale.Items[0].UnitCostUzs);
        var document = new SaleReturn { Reason = "Размер", RefundAmountUzs = 250,
            Items = [new() { SaleItemId = sale.Items[0].Id, Quantity = 1, Disposition = ReturnDisposition.Restock }] };
        var returns = new SalesReturnService(catalog, store);
        await returns.CreateAsync(sale, document);
        await returns.CreateAsync(sale, document);
        Assert.Equal(450m, sale.CostUzs);
        Assert.Equal(1350m, FifoCostCalculator.Value((await catalog.GetProductAsync(product.Id))!.Variants[0]).TotalValue);
        var resale = new Sale { Number = "S-2", Items = [new() { ProductId = product.Id, ProductVariantId = variant.Id, Quantity = 1, UnitPriceUzs = 250 }] };
        await inventory.ReserveAsync(resale);
        await inventory.MarkShippedAsync(resale);
        await inventory.CompleteAsync(resale);
        Assert.Equal(150m, resale.CostUzs);
        var actual = (await catalog.GetProductAsync(product.Id))!.Variants[0];
        Assert.Equal(8, actual.Quantity);
        Assert.Equal(1200m, FifoCostCalculator.Value(actual).TotalValue);
        Assert.Equal(1200m, store.Movements.Sum(x => x.ValueDelta));
        var report = new AnalyticsService().Build([sale, resale], [product], null, null);
        Assert.Equal(600m, report.CostUzs);
    }

    [Theory]
    [InlineData(2, 8, 125)]
    [InlineData(10, 0, 0)]
    public async Task Receipt_defects_are_absorbed_by_saleable_units_or_recognized_as_loss(int defects, int accepted, decimal unitCost)
    {
        var product = new Product { Name = "Товар", Sku = "DEF", Variants = [new()] };
        var catalog = new CloningCatalog(product);
        var store = new MemoryInventoryStore(catalog);
        var purchase = new Purchase { Number = "P", CurrencyCode = "UZS", Items = [new() { ProductId = product.Id, ProductVariantId = product.Variants[0].Id, Quantity = 10, UnitPrice = 100 }] };
        await new PurchaseReceivingService(catalog, store).ReceiveAsync(purchase, [new(purchase.Items[0].Id, 10, defects)]);
        var actual = (await catalog.GetProductAsync(product.Id))!.Variants[0];
        FifoCostCalculator.Validate(actual);
        Assert.Equal(accepted, actual.Quantity);
        if (accepted > 0) Assert.Equal(unitCost, Assert.Single(actual.Layers).UnitCost);
        else
        {
            Assert.Empty(actual.Layers);
            Assert.Equal(1000m, Assert.Single(purchase.StockValuations).ExpenseDelta);
        }
    }

    [Fact]
    public async Task Unknown_adjustment_creates_unknown_layer_and_fifo_loss_uses_original_value()
    {
        var product = new Product { Sku = "A", Variants = [new() { Quantity = 0 }] };
        var catalog = new CloningCatalog(product);
        var store = new MemoryInventoryStore(catalog);
        var service = new StockAdjustmentService(catalog, store);
        var request = new StockAdjustmentRequest { ProductId = product.Id, ProductVariantId = product.Variants[0].Id,
            Reason = StockAdjustmentReason.OpeningBalance, NewQuantity = 2, Note = "Начальный остаток", UnitCost = 100 };
        await service.AdjustAsync(request);
        request.NewQuantity = 3; request.UnitCost = null;
        var unknown = await service.AdjustAsync(request);
        Assert.Null(unknown.ValueDelta);
        Assert.Equal(1, unknown.UnvaluedQuantity);
        request.NewQuantity = 1; request.Reason = StockAdjustmentReason.Loss;
        var loss = await service.AdjustAsync(request);
        Assert.Equal(-200m, loss.ValueDelta);
        var valuation = FifoCostCalculator.Value((await catalog.GetProductAsync(product.Id))!.Variants[0]);
        Assert.Equal(1, valuation.UnvaluedQuantity);
        Assert.Null(valuation.TotalValue);
    }

    [Fact]
    public async Task Nonempty_stock_without_layers_is_rejected_without_inventing_migration()
    {
        var product = new Product { Sku = "BROKEN", Variants = [new() { Quantity = 0 }] };
        var catalog = new CloningCatalog(product);
        product.Variants[0].StockLayerVersion = 0;
        product.Variants[0].Quantity = 2;
        catalog.Put(product);
        var store = new MemoryInventoryStore(catalog);
        var sale = new Sale { Items = [new() { ProductId = product.Id, ProductVariantId = product.Variants[0].Id, Quantity = 1, UnitPriceUzs = 1 }] };
        await Assert.ThrowsAsync<InventoryException>(() => new SalesInventoryService(catalog, store).ReserveAsync(sale));
        Assert.Empty(store.Commits);
    }
}
