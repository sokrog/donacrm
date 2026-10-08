using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Services;

public sealed class SupplierAnalyticsService
{
    public IReadOnlyList<SupplierAnalytics> Calculate(IEnumerable<Supplier> suppliers, IEnumerable<Purchase> purchases, PurchaseHistoryData history)
    {
        var active = purchases.Where(x => x.Status is PurchaseStatus.Ordered or PurchaseStatus.ChinaWarehouse or PurchaseStatus.Shipped or PurchaseStatus.PartiallyReceived or PurchaseStatus.Received).ToList();
        return suppliers.Select(supplier => CalculateSupplier(supplier, active.Where(x => Matches(x, supplier)).ToList(), history)).ToList();
    }

    public IReadOnlyList<ProductSupplierComparison> CompareProducts(PurchaseHistoryData history) => history.ProductCosts
        .GroupBy(x => new { x.ProductId, x.ProductName, x.Sku, x.SupplierId, CurrencyCode = CurrencyCodes.Normalize(x.CurrencyCode, "CNY"), Name = string.IsNullOrWhiteSpace(x.SupplierName) ? "Без поставщика" : x.SupplierName })
        .Select(group => new ProductSupplierComparison
        {
            ProductId = group.Key.ProductId,
            ProductName = group.Key.ProductName,
            Sku = group.Key.Sku,
            SupplierId = group.Key.SupplierId,
            SupplierName = group.Key.Name,
            ReceiptCount = group.Select(x => x.ReceiptId).Distinct().Count(),
            Quantity = group.Sum(x => x.Quantity),
            AverageUnitPrice = WeightedAverage(group, x => x.UnitPrice),
            CurrencyCode = group.Key.CurrencyCode,
            AverageUnitCostUzs = WeightedAverage(group, x => x.UnitLandedCostUzs)
        }).ToList();

    private static SupplierAnalytics CalculateSupplier(Supplier supplier, List<Purchase> purchases, PurchaseHistoryData history)
    {
        var ordered = purchases.Sum(x => x.Items.Sum(item => item.Quantity ?? 0));
        var received = purchases.Sum(x => x.Receipts.Sum(receipt => receipt.ReceivedQuantity));
        var defects = purchases.Sum(x => x.Receipts.Sum(receipt => receipt.DefectQuantity));
        var stocked = purchases.Sum(x => x.Receipts.Sum(receipt => receipt.StockedQuantity));
        var delivered = purchases.Where(x => x.Status == PurchaseStatus.Received && x.EstimatedDeliveryDate is not null && x.Receipts.Count > 0)
            .Select(x => (decimal)(x.Receipts.Max(r => r.ReceivedAt).ToLocalTime().Date - x.EstimatedDeliveryDate!.Value.Date).TotalDays).ToList();
        var costs = history.ProductCosts.Where(x => x.SupplierId == supplier.Id || (x.SupplierId is null && x.SupplierName.Equals(supplier.Name, StringComparison.OrdinalIgnoreCase))).ToList();
        var defectRate = received == 0 ? 0 : Math.Round(defects * 100m / received, 1);
        var completeness = ordered == 0 ? 0 : Math.Round(Math.Min(received, ordered) * 100m / ordered, 1);
        var scoreParts = new List<(decimal Score, decimal Weight)>();
        if (received > 0) scoreParts.Add((Math.Max(0, 100 - defectRate * 5), 35));
        if (ordered > 0) scoreParts.Add((completeness, 35));
        if (delivered.Count > 0) scoreParts.Add((Math.Max(0, 100 - Math.Max(0, delivered.Average()) * 5), 30));
        return new SupplierAnalytics
        {
            SupplierId = supplier.Id,
            SupplierName = supplier.Name,
            PurchaseCount = purchases.Count,
            ReceiptCount = purchases.Sum(x => x.Receipts.Count),
            OrderedQuantity = ordered,
            ReceivedQuantity = received,
            DefectQuantity = defects,
            StockedQuantity = stocked,
            DefectRatePercent = defectRate,
            CompletenessPercent = completeness,
            DeliverySamples = delivered.Count,
            AverageDelayDays = delivered.Count == 0 ? 0 : Math.Round(delivered.Average(), 1),
            OnTimePercent = delivered.Count == 0 ? 0 : Math.Round(delivered.Count(x => x <= 0) * 100m / delivered.Count, 1),
            AverageUnitCostUzs = WeightedAverage(costs, x => x.UnitLandedCostUzs),
            ReliabilityScore = scoreParts.Count == 0 ? null : Math.Round(scoreParts.Sum(x => x.Score * x.Weight) / scoreParts.Sum(x => x.Weight), 0)
        };
    }

    private static bool Matches(Purchase purchase, Supplier supplier) => purchase.SupplierId == supplier.Id || (purchase.SupplierId is null && purchase.SupplierName?.Equals(supplier.Name, StringComparison.OrdinalIgnoreCase) == true);
    private static decimal WeightedAverage(IEnumerable<ProductCostHistoryEntry> source, Func<ProductCostHistoryEntry, decimal> selector)
    {
        var values = source.ToList(); var quantity = values.Sum(x => x.Quantity);
        return quantity == 0 ? 0 : Math.Round(values.Sum(x => selector(x) * x.Quantity) / quantity, 2);
    }
}
