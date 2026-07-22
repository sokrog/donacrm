using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Services;

public sealed class ProductStatusService
{
    public ProductStatus? Calculate(Product product, BusinessSettings settings)
    {
        if (!settings.AutoUpdateStockStatus || product.Status == ProductStatus.Archived || product.Variants.Count == 0) return product.Status;
        var quantity = settings.CountReservedAsUnavailable ? product.Variants.Sum(x => x.AvailableQuantity) : product.Quantity;
        if (quantity <= 0) return settings.ZeroStockStatus;
        if (quantity <= Math.Max(0, settings.LowStockThreshold)) return ProductStatus.LowStock;
        return ProductStatus.InStock;
    }
}
