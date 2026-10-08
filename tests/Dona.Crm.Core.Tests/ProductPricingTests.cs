using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;

namespace Dona.Crm.Core.Tests;

public sealed class ProductPricingTests
{
    [Fact]
    public void Weighted_cost_uses_physical_stock_and_rounding_recalculates_actual_markup()
    {
        var product = new Product { Variants = [new() { Quantity = 10, ReservedQuantity = 4, StockLayerVersion = 1,
            Layers = [new() { InitialQuantity = 4, RemainingQuantity = 4, InitialValue = 400000, RemainingValue = 400000 },
                      new() { InitialQuantity = 6, RemainingQuantity = 6, InitialValue = 720000, RemainingValue = 720000 }] }] };
        var quote = ProductPricingService.BuildQuote(product, PricingBasis.RemainingStock);
        Assert.Equal(10, quote.Quantity);
        Assert.Equal(112000, quote.UnitCost);
        Assert.Equal(168000, SellingPriceCalculator.Price(quote.UnitCost, 50, 1));
        Assert.Equal(170000, SellingPriceCalculator.Price(quote.UnitCost, 50, 5000));
        Assert.Equal(51.785714285714285714285714290m, SellingPriceCalculator.Markup(170000, quote.UnitCost));
        Assert.Equal(100000, ProductPricingService.BuildQuote(product, PricingBasis.Layer, layerId: product.Variants[0].Layers[0].Id).UnitCost);
    }

    [Fact]
    public void Incomplete_cost_and_missing_layers_never_produce_false_average()
    {
        var product = new Product { Variants = [new() { Quantity = 4, Layers = [new() { RemainingQuantity = 2, RemainingValue = 100 }, new() { RemainingQuantity = 1 }] }] };
        var quote = ProductPricingService.BuildQuote(product, PricingBasis.RemainingStock);
        Assert.Equal(4, quote.Quantity);
        Assert.Equal(2, quote.UnknownQuantity);
        Assert.Null(quote.UnitCost);
        Assert.Throws<InvalidOperationException>(() => SellingPriceCalculator.Price(quote.UnitCost, 50, 1));
        Assert.Null(SellingPriceCalculator.Markup(100, 0));
    }

    [Fact]
    public void Purchase_estimate_merges_repeated_product_rows_and_received_basis_uses_remaining_lots()
    {
        var product = new Product { Variants = [new()] };
        var purchase = new Purchase { CurrencyCode = "UZS", Items = [new() { ProductId = product.Id, Quantity = 2, UnitPrice = 100 }, new() { ProductId = product.Id, Quantity = 3, UnitPrice = 200 }] };
        var estimate = ProductPricingService.BuildQuote(product, PricingBasis.PurchaseEstimate, purchase);
        Assert.Equal(5, estimate.Quantity);
        Assert.Equal(160, estimate.UnitCost);
        product.Variants[0].Quantity = 1;
        product.Variants[0].Layers.Add(new() { PurchaseId = purchase.Id, RemainingQuantity = 1, RemainingValue = 100 });
        var received = ProductPricingService.BuildQuote(product, PricingBasis.PurchaseStock, purchase);
        Assert.Equal(1, received.Quantity);
        Assert.Equal(100, received.UnitCost);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(1.5)] [InlineData(1000000001)]
    public void Invalid_manual_prices_are_rejected(decimal price) => Assert.Throws<InvalidOperationException>(() => SellingPriceCalculator.ValidatePrice(price));

    [Theory]
    [InlineData(-100)] [InlineData(100001)]
    public void Invalid_markup_is_rejected(decimal markup) => Assert.Throws<InvalidOperationException>(() => SellingPriceCalculator.Price(100, markup, 1));
}
