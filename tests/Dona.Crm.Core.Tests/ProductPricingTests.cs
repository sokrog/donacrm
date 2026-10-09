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

    [Fact]
    public void Closing_and_reserving_do_not_require_review_but_changed_unit_cost_does()
    {
        var product = new Product { Variants = [new() { Quantity = 2 }] };
        var purchase = new Purchase { CurrencyCode = "UZS", Items = [new() { ProductId = product.Id, Quantity = 2, UnitPrice = 100 }] };
        var variant = product.Variants[0];
        variant.Layers.Add(new() { PurchaseId = purchase.Id, RemainingQuantity = 2, RemainingValue = 200 });
        var original = ProductPricingService.BuildQuote(product, PricingBasis.PurchaseStock, purchase);
        purchase.ClosedAt = DateTimeOffset.UtcNow;
        purchase.IsCostFinalized = true;
        variant.ReservedQuantity = 1;
        variant.StockLayerVersion++;
        var closed = ProductPricingService.BuildQuote(product, PricingBasis.PurchaseStock, purchase);
        Assert.NotEqual(original.Fingerprint, closed.Fingerprint);
        Assert.False(ProductPricingService.CostNeedsReview(original, closed));
        variant.Quantity = variant.Layers[0].RemainingQuantity = 1;
        variant.Layers[0].RemainingValue = 100;
        Assert.False(ProductPricingService.CostNeedsReview(original, ProductPricingService.BuildQuote(product, PricingBasis.PurchaseStock, purchase)));
        variant.Layers[0].RemainingValue = 200;
        Assert.True(ProductPricingService.CostNeedsReview(original, ProductPricingService.BuildQuote(product, PricingBasis.PurchaseStock, purchase)));
        variant.Quantity = variant.Layers[0].RemainingQuantity = 0;
        variant.Layers[0].RemainingValue = 0;
        Assert.False(ProductPricingService.CostNeedsReview(original, ProductPricingService.BuildQuote(product, PricingBasis.PurchaseStock, purchase)));
    }

    [Fact]
    public void Purchase_pricing_excludes_exhausted_stock_after_receiving_and_keeps_partial_stock()
    {
        var product = new Product { Variants = [new()] };
        var purchase = new Purchase { CurrencyCode = "UZS", Items = [new() { ProductId = product.Id, Quantity = 2, UnitPrice = 100 }] };
        Assert.Equal(PricingBasis.PurchaseEstimate, ProductPricingService.BuildPurchaseQuote(product, purchase)!.Basis);
        purchase.Items[0].ReceivedQuantity = 2;
        Assert.Null(ProductPricingService.BuildPurchaseQuote(product, purchase));
        purchase.ReceivingCompletedAt = DateTimeOffset.UtcNow;
        purchase.ClosedAt = DateTimeOffset.UtcNow;
        Assert.Null(ProductPricingService.BuildPurchaseQuote(product, purchase));
        product.Variants[0].Layers.Add(new() { PurchaseId = Guid.NewGuid(), RemainingQuantity = 3, RemainingValue = 900 });
        Assert.Null(ProductPricingService.BuildPurchaseQuote(product, purchase));
        product.Variants[0].Layers.Add(new() { PurchaseId = purchase.Id, RemainingQuantity = 1, RemainingValue = 100 });
        var remaining = ProductPricingService.BuildPurchaseQuote(product, purchase)!;
        Assert.Equal(PricingBasis.PurchaseStock, remaining.Basis);
        Assert.Equal(1, remaining.Quantity);
        Assert.Equal(100, remaining.UnitCost);
    }

    [Fact]
    public void Completed_receiving_without_goods_does_not_restore_planned_quantities()
    {
        var product = new Product();
        var purchase = new Purchase { CurrencyCode = "UZS", ReceivingCompletedAt = DateTimeOffset.UtcNow,
            Items = [new() { ProductId = product.Id, Quantity = 2, UnitPrice = 100 }] };
        Assert.Null(ProductPricingService.BuildPurchaseQuote(product, purchase));
    }
}
