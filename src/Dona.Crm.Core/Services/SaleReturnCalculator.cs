using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Services;

public static class SaleReturnCalculator
{
    public static decimal GoodsRefund(Sale sale, SaleReturn document)
    {
        if (sale.SubtotalUzs <= 0) return 0;
        var goodsTotal = sale.SubtotalUzs - sale.AppliedDiscountUzs;
        decimal previousWeight = 0, previousValue = 0, result = 0;
        foreach (var item in sale.Items)
        {
            previousWeight += (item.UnitPriceUzs ?? 0) * (item.Quantity ?? 0);
            var cumulative = Math.Round(goodsTotal * previousWeight / sale.SubtotalUzs, 2, MidpointRounding.AwayFromZero);
            var itemValue = cumulative - previousValue;
            previousValue = cumulative;
            if (item.Quantity is null or <= 0) continue;
            var quantity = document.Items.Where(x => x.SaleItemId == item.Id && x.Disposition is ReturnDisposition.Restock or ReturnDisposition.Defect)
                .Sum(x => Math.Max(0, x.Quantity ?? 0));
            var previous = item.ReturnedQuantity;
            result += Math.Round(itemValue * (previous + quantity) / item.Quantity.Value, 2, MidpointRounding.AwayFromZero)
                - Math.Round(itemValue * previous / item.Quantity.Value, 2, MidpointRounding.AwayFromZero);
        }
        return result;
    }

    public static decimal DeliveryAvailable(Sale sale) => Math.Max(0,
        (sale.DeliveryChargeUzs ?? 0) - sale.Returns.Sum(x => x.DeliveryRefundUzs));
}
