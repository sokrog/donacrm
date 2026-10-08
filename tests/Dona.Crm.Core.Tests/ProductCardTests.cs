using System.Text.Json;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;

namespace Dona.Crm.Core.Tests;

public sealed class ProductCardTests
{
    [Fact]
    public void Purchase_price_uses_latest_matching_currency_and_excludes_current_and_cancelled()
    {
        var productId = Guid.NewGuid();
        var current = new Purchase { CurrencyCode = "USD", Items = [new() { ProductId = productId, UnitPrice = 999 }] };
        Purchase Order(int day, string currency, decimal price, PurchaseStatus status = PurchaseStatus.Received) => new()
        {
            OrderedAt = DateTimeOffset.UnixEpoch.AddDays(day), CurrencyCode = currency, Status = status,
            Items = [new() { ProductId = productId, UnitPrice = price }]
        };
        var history = new[] { current, Order(1, "USD", 10), Order(2, "USD", 15), Order(3, "CNY", 20), Order(4, "USD", 30, PurchaseStatus.Cancelled) };
        Assert.Equal(15, ProductPurchaseDefaults.LastPrice(productId, current, history));
        Assert.Null(ProductPurchaseDefaults.LastPrice(Guid.NewGuid(), current, history));
        current.CurrencyCode = "UZS";
        Assert.Null(ProductPurchaseDefaults.LastPrice(productId, current, history));
    }

    [Theory]
    [InlineData(null, 0.75)]
    [InlineData(0.5, 0.5)]
    public async Task Receipt_fills_only_missing_product_weight(double? weight, double expected)
    {
        var product = new Product { Sku = "WEIGHT", UnitWeightKg = (decimal?)weight, Variants = [new() { Quantity = 0 }] };
        var catalog = new CloningCatalog(product);
        var store = new MemoryInventoryStore(catalog);
        var purchase = new Purchase { Number = "WEIGHT-1", CurrencyCode = "UZS", Items = [new()
        {
            ProductId = product.Id, ProductVariantId = product.Variants[0].Id, Quantity = 1, UnitPrice = 100, UnitWeightKg = 0.75m
        }] };
        await new PurchaseReceivingService(catalog, store).ReceiveAsync(purchase, [new(purchase.Items[0].Id, 1, 0)]);
        var saved = (await catalog.GetProductAsync(product.Id))!;
        Assert.Equal((decimal)expected, saved.UnitWeightKg);
        Assert.Equal(100, FifoCostCalculator.Value(saved.Variants[0]).TotalValue);
        var json = JsonSerializer.Serialize(saved);
        foreach (var field in new[] { "PlannedPurchasePrice", "PurchaseCurrencyCode", "RateToUzs", "AgentCommissionPercent", "DeliveryCostUzs", "CostPurchaseId", "CostUzs", "ProfitUzs", "MarkupPercent", "PurchasePriceCny", "CnyRateUzs", "SupplierName", "AllowOrderWhenUnavailable" })
            Assert.DoesNotContain($"\"{field}\"", json);
    }
}
