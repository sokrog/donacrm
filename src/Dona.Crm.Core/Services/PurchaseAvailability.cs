using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Services;

public static class PurchaseAvailability
{
    public static int Expected(Guid productId, IEnumerable<Purchase> purchases) => purchases
        .Where(x => x.ClosedAt is null && x.Status is PurchaseStatus.Ordered or PurchaseStatus.ChinaWarehouse or PurchaseStatus.Shipped or PurchaseStatus.PartiallyReceived)
        .SelectMany(x => x.Items).Where(x => x.ProductId == productId).Sum(x => x.MissingQuantity);
}
