using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Services;

public static class PurchaseAvailability
{
    public static bool IsAwaitingDelivery(Purchase purchase) => purchase.ClosedAt is null
        && purchase.ReceivingCompletedAt is null
        && purchase.Status is PurchaseStatus.Ordered or PurchaseStatus.ChinaWarehouse or PurchaseStatus.Shipped or PurchaseStatus.PartiallyReceived
        && purchase.Items.Any(item => item.MissingQuantity > 0);

    public static int Expected(Guid productId, IEnumerable<Purchase> purchases) => purchases
        .Where(IsAwaitingDelivery)
        .SelectMany(x => x.Items).Where(x => x.ProductId == productId).Sum(x => x.MissingQuantity);
}
