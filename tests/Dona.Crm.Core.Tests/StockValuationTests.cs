using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;

namespace Dona.Crm.Core.Tests;

public sealed class StockValuationTests
{
    private static (Product Product, ProductVariant Variant, StockLayer Layer, CloningCatalog Catalog, MemoryInventoryStore Store) Setup(int remaining = 4)
    {
        var variant = new ProductVariant { Quantity = 0 };
        var layer = StockLayerOperations.Add(variant, 10, null, StockLayerSource.OpeningBalance, DateTimeOffset.UtcNow.AddMonths(-2), "Начальный остаток");
        if (remaining < 10) FifoCostCalculator.Consume(variant, 10 - remaining);
        var product = new Product { Name = "Товар", Sku = "VALUE", Variants = [variant] };
        var catalog = new CloningCatalog(product);
        return (product, variant, layer, catalog, new MemoryInventoryStore(catalog));
    }

    [Theory]
    [InlineData(4, 1000, 400, 600)]
    [InlineData(0, 1000, 0, 1000)]
    [InlineData(10, 1000, 1000, 0)]
    [InlineData(4, 0, 0, 0)]
    [InlineData(4, 10.01, 4, 6.01)]
    public async Task Initial_value_allocates_exact_total_and_retry_is_idempotent(int remaining, decimal total, decimal stock, decimal expense)
    {
        var (product, variant, layer, catalog, store) = Setup(remaining);
        var service = new StockValuationService(catalog, store);
        var request = new InitialStockValuationRequest(product.Id, variant.Id, layer.Id, Guid.NewGuid(), 0, remaining, total);
        var result = await service.ValueAsync(request);
        Assert.Equal(stock, result.InventoryValueDelta);
        Assert.Equal(expense, result.ExpenseDelta);
        Assert.Null(result.PreviousValue);
        Assert.Equal(result, await service.ValueAsync(request));
        Assert.Single(store.Commits);
        var saved = (await catalog.GetProductAsync(product.Id))!;
        Assert.Equal(total, saved.Variants[0].Layers[0].InitialValue);
        Assert.Equal(stock, saved.Variants[0].Layers[0].RemainingValue);
        Assert.Equal(remaining, saved.Variants[0].Quantity);
        Assert.Equal(1, saved.Variants[0].Layers[0].ValuationRevision);
        Assert.Empty(store.Movements);
        await Assert.ThrowsAsync<InventoryException>(() => service.ValueAsync(request with { TotalValue = total + 1 }));
        await Assert.ThrowsAsync<InventoryException>(() => service.ValueAsync(request with { OperationId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Stale_preview_invalid_value_and_purchase_layer_are_rejected()
    {
        var (product, variant, layer, catalog, store) = Setup();
        var service = new StockValuationService(catalog, store);
        var request = new InitialStockValuationRequest(product.Id, variant.Id, layer.Id, Guid.NewGuid(), 0, 4, 1000);
        await Assert.ThrowsAsync<InventoryException>(() => service.ValueAsync(request with { ExpectedRemainingQuantity = 5 }));
        await Assert.ThrowsAsync<InventoryException>(() => service.ValueAsync(request with { TotalValue = -1 }));
        await Assert.ThrowsAsync<InventoryException>(() => service.ValueAsync(request with { TotalValue = 1.001m }));
        layer.Source = StockLayerSource.PurchaseReceipt;
        catalog.Put(product);
        await Assert.ThrowsAsync<InventoryException>(() => service.ValueAsync(request));
        Assert.Empty(store.Commits);
    }

    [Fact]
    public async Task Failed_commit_can_be_retried_without_partial_valuation()
    {
        var (product, variant, layer, catalog, store) = Setup();
        var service = new StockValuationService(catalog, store);
        var request = new InitialStockValuationRequest(product.Id, variant.Id, layer.Id, Guid.NewGuid(), 0, 4, 1000);
        store.FailWith = new IOException("Сбой записи");
        await Assert.ThrowsAsync<IOException>(() => service.ValueAsync(request));
        var saved = (await catalog.GetProductAsync(product.Id))!;
        Assert.Null(saved.Variants[0].Layers[0].InitialValue);
        Assert.Empty(saved.StockValuations);
        store.FailWith = null;
        await service.ValueAsync(request);
        Assert.Single(store.Commits);
    }

    [Fact]
    public async Task Valuation_keeps_old_sale_unknown_and_return_reverses_current_expense()
    {
        var (product, variant, layer, catalog, store) = Setup(10);
        var sale = new Sale { Items = [new() { ProductId = product.Id, ProductVariantId = variant.Id, Quantity = 6, UnitPriceUzs = 200 }] };
        var inventory = new SalesInventoryService(catalog, store);
        await inventory.ReserveAsync(sale);
        await inventory.MarkShippedAsync(sale);
        await inventory.CompleteAsync(sale);
        sale.CompletedAt = DateTimeOffset.UtcNow.AddMonths(-1);
        sale.ShippedAt = sale.CompletedAt;
        var oldDate = sale.CompletedAt.Value.LocalDateTime;
        var service = new StockValuationService(catalog, store);
        await service.ValueAsync(new(product.Id, variant.Id, layer.Id, Guid.NewGuid(), 0, 4, 1000));
        var saved = (await catalog.GetProductAsync(product.Id))!;
        Assert.Null(sale.Items[0].Consumptions[0].TotalCost);
        var old = new AnalyticsService().Build([sale], [saved], oldDate.Date, oldDate.Date);
        Assert.True(old.HasUnknownCost);
        Assert.Equal(0, old.PeriodExpensesUzs);
        await inventory.ReturnAsync(sale);
        saved = (await catalog.GetProductAsync(product.Id))!;
        Assert.Equal(1000, saved.Variants[0].Layers[0].RemainingValue);
        Assert.Equal(0, saved.StockValuations.Sum(x => x.ExpenseDelta));
        Assert.Equal(100, FifoCostCalculator.Cost(FifoCostCalculator.Preview(saved.Variants[0], 1)).TotalValue);
    }
}
