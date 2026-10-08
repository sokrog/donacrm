using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Services;

public static class ProductPurchaseDefaults
{
    public static decimal? LastPrice(Guid productId, Purchase current, IEnumerable<Purchase> purchases) => purchases
        .Where(x => x.Id != current.Id && x.Status != PurchaseStatus.Cancelled && x.CurrencyCode == current.CurrencyCode)
        .OrderByDescending(x => x.OrderedAt)
        .SelectMany(x => x.Items)
        .FirstOrDefault(x => x.ProductId == productId)?.UnitPrice;
}
