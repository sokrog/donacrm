using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Services;

public sealed class InventoryAnalyticsService
{
    public InventoryAnalyticsReport Build(IEnumerable<Product> products, IEnumerable<Sale> sales, IEnumerable<StockMovement> movements, BusinessSettings settings, DateTimeOffset? now = null)
    {
        var current = now ?? DateTimeOffset.Now;
        var completed = sales.Where(x => x.Status is SaleStatus.Completed or SaleStatus.Returned).ToList();
        var movementList = movements.ToList();
        var rows = new List<InventoryAnalyticsRow>();
        foreach (var product in products.Where(x => x.Status != ProductStatus.Archived))
        {
            foreach (var variant in product.Variants)
            {
                var relevantSales = completed.SelectMany(sale => sale.Items.Where(item => item.ProductVariantId == variant.Id || (item.ProductVariantId is null && product.Variants.Count == 1 && item.ProductId == product.Id)).Select(item => new SaleFact(sale.CreatedAt, NetSold(item)))).Where(x => x.Quantity > 0).ToList();
                var lastSale = relevantSales.Count == 0 ? null : relevantSales.Max(x => x.CreatedAt) as DateTimeOffset?;
                var lastReceipt = movementList.Where(x => x.ProductVariantId == variant.Id && x.Type == StockMovementType.PurchaseReceipt && x.QuantityDelta > 0).OrderByDescending(x => x.CreatedAt).FirstOrDefault()?.CreatedAt;
                var lastActivity = new[] { lastSale, lastReceipt, product.CreatedAt }.Where(x => x is not null).Max();
                var inactive = Math.Max(0, (int)(current.ToLocalTime().Date - lastActivity!.Value.ToLocalTime().Date).TotalDays);
                var sold30 = SoldSince(relevantSales, current.AddDays(-30));
                var sold60 = SoldSince(relevantSales, current.AddDays(-60));
                var sold90 = SoldSince(relevantSales, current.AddDays(-90));
                var available = variant.AvailableQuantity;
                rows.Add(new InventoryAnalyticsRow
                {
                    ProductId = product.Id, ProductVariantId = variant.Id, ProductName = product.Name, Sku = product.Sku, Category = string.IsNullOrWhiteSpace(product.Category) ? "Без категории" : product.Category,
                    Color = variant.Color, Size = variant.Size, Quantity = variant.Quantity ?? 0, ReservedQuantity = variant.ReservedQuantity, AvailableQuantity = available,
                    UnitCostUzs = product.CostUzs, SellingPriceUzs = product.SellingPriceUzs ?? 0, InventoryCostUzs = product.CostUzs * (variant.Quantity ?? 0), PotentialRevenueUzs = (product.SellingPriceUzs ?? 0) * (variant.Quantity ?? 0),
                    Sold30Days = sold30, Sold60Days = sold60, Sold90Days = sold90, DaysOfCover = sold90 == 0 ? null : Math.Round(available / (sold90 / 90m), 0),
                    LastSaleAt = lastSale, LastReceiptAt = lastReceipt, InactiveDays = inactive, IsStale = available > 0 && inactive >= settings.StaleInventoryDays, IsLowStock = available > 0 && available <= settings.LowStockThreshold
                });
            }
        }
        return new InventoryAnalyticsReport
        {
            Rows = rows,
            PhysicalUnits = rows.Sum(x => x.Quantity), ReservedUnits = rows.Sum(x => x.ReservedQuantity), AvailableUnits = rows.Sum(x => x.AvailableQuantity),
            InventoryCostUzs = rows.Sum(x => x.InventoryCostUzs), PotentialRevenueUzs = rows.Sum(x => x.PotentialRevenueUzs), PotentialProfitUzs = rows.Sum(x => x.PotentialRevenueUzs - x.InventoryCostUzs),
            StaleInventoryCostUzs = rows.Where(x => x.IsStale).Sum(x => x.InventoryCostUzs), LowStockVariants = rows.Count(x => x.IsLowStock)
        };
    }

    private static int NetSold(SaleItem item) => Math.Max(0, (item.SoldQuantity > 0 ? item.SoldQuantity : item.Quantity ?? 0) - item.ReturnedQuantity);
    private static int SoldSince(IEnumerable<SaleFact> sales, DateTimeOffset since) => sales.Where(x => x.CreatedAt >= since).Sum(x => x.Quantity);
    private sealed record SaleFact(DateTimeOffset CreatedAt, int Quantity);
}
