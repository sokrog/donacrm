using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;

namespace Dona.Crm.Core.Tests;

internal static class FifoFixtures
{
    // Existing service fixtures describe an opening balance; give that balance its explicit layer.
    public static Product OpeningBalance(Product product)
    {
        foreach (var variant in product.Variants.Where(x => x.StockLayerVersion == 0))
        {
            variant.StockLayerVersion = FifoCostCalculator.CurrentVersion;
            variant.Quantity ??= 0;
            if (variant.Quantity > 0) variant.Layers.Add(new()
            {
                Source = StockLayerSource.OpeningBalance, InitialQuantity = variant.Quantity.Value,
                RemainingQuantity = variant.Quantity.Value, InitialValue = product.CostUzs * variant.Quantity,
                RemainingValue = product.CostUzs * variant.Quantity
            });
        }
        return product;
    }

    public static void CompletedSale(ProductVariant variant, SaleItem item)
    {
        var layer = new StockLayer { Source = StockLayerSource.OpeningBalance,
            InitialQuantity = (variant.Quantity ?? 0) + item.SoldQuantity, RemainingQuantity = variant.Quantity ?? 0,
            InitialValue = ((variant.Quantity ?? 0) + item.SoldQuantity) * item.UnitCostUzs,
            RemainingValue = (variant.Quantity ?? 0) * item.UnitCostUzs };
        variant.StockLayerVersion = FifoCostCalculator.CurrentVersion;
        variant.Layers = [layer];
        item.Consumptions = [new(Guid.NewGuid(), layer.Id, item.SoldQuantity, item.UnitCostUzs, item.UnitCostUzs * item.SoldQuantity, 0)];
    }
}
