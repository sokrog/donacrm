using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Services;

public sealed class InventoryAnalyticsService
{
    public InventoryAnalyticsReport Build(IEnumerable<Product> products, IEnumerable<Sale> sales, IEnumerable<StockMovement> movements, BusinessSettings settings, DateTimeOffset? now = null)
    {
        var current = now ?? DateTimeOffset.Now;
        var facts = SaleFinancialEvents.Build(sales);
        var movementList = movements.ToList();
        var rows = new List<InventoryAnalyticsRow>();
        foreach (var product in products.Where(x => x.Status != ProductStatus.Archived))
        {
            foreach (var variant in product.Variants)
            {
                var relevantSales = facts.Where(x => x.Item.ProductVariantId == variant.Id || x.Item.ProductVariantId is null && product.Variants.Count == 1 && x.Item.ProductId == product.Id)
                    .Select(x => new SaleFact(x.At, x.Quantity)).ToList();
                var lastSale = relevantSales.Where(x => x.Quantity > 0).Select(x => (DateTimeOffset?)x.CreatedAt).Max();
                var lastReceipt = movementList.Where(x => x.ProductVariantId == variant.Id && x.Type == StockMovementType.PurchaseReceipt && x.QuantityDelta > 0).OrderByDescending(x => x.CreatedAt).FirstOrDefault()?.CreatedAt;
                var lastActivity = new[] { lastSale, lastReceipt, product.CreatedAt }.Where(x => x is not null).Max();
                var inactive = Math.Max(0, (int)(current.ToLocalTime().Date - lastActivity!.Value.ToLocalTime().Date).TotalDays);
                var sold30 = SoldSince(relevantSales, current.AddDays(-30));
                var sold60 = SoldSince(relevantSales, current.AddDays(-60));
                var sold90 = SoldSince(relevantSales, current.AddDays(-90));
                var available = variant.AvailableQuantity;
                var valuation = variant.StockLayerVersion == FifoCostCalculator.CurrentVersion
                    ? FifoCostCalculator.Value(variant) : new StockCostSummary(0, variant.Quantity ?? 0);
                var stocked = variant.Layers.Where(x => x.RemainingQuantity > 0).ToList();
                int Age(StockLayer layer) => Math.Max(0, (current.LocalDateTime.Date - layer.ReceivedAt!.Value.LocalDateTime.Date).Days);
                var staleCost = variant.StockLayerVersion == FifoCostCalculator.CurrentVersion
                    ? stocked.Where(x => x.ReceivedAt is not null && Age(x) >= settings.StaleInventoryDays).Sum(x => x.RemainingValue ?? 0)
                    : inactive >= settings.StaleInventoryDays ? valuation.KnownValue : 0;
                var stale = variant.StockLayerVersion == FifoCostCalculator.CurrentVersion
                    ? stocked.Any(x => x.ReceivedAt is not null && Age(x) >= settings.StaleInventoryDays)
                    : available > 0 && inactive >= settings.StaleInventoryDays;
                rows.Add(new InventoryAnalyticsRow
                {
                    ProductId = product.Id, ProductVariantId = variant.Id, ProductName = product.Name, Sku = product.Sku, Category = string.IsNullOrWhiteSpace(product.Category) ? "Без категории" : product.Category,
                    Color = variant.Color, Size = variant.Size, Quantity = variant.Quantity ?? 0, ReservedQuantity = variant.ReservedQuantity, AvailableQuantity = available,
                    UnitCostUzs = variant.Quantity > 0 ? valuation.KnownValue / variant.Quantity.Value : 0, UnvaluedQuantity = valuation.UnvaluedQuantity,
                    SellingPriceUzs = product.SellingPriceUzs ?? 0, InventoryCostUzs = valuation.KnownValue, PotentialRevenueUzs = (product.SellingPriceUzs ?? 0) * (variant.Quantity ?? 0),
                    Sold30Days = sold30, Sold60Days = sold60, Sold90Days = sold90, DaysOfCover = sold90 == 0 ? null : Math.Round(available / (sold90 / 90m), 0),
                    LastSaleAt = lastSale, LastReceiptAt = lastReceipt, InactiveDays = inactive, IsStale = stale, StaleCostUzs = staleCost,
                    OldestLayerAgeDays = stocked.Where(x => x.ReceivedAt is not null).Select(x => (int?)Age(x)).Max(),
                    IsLowStock = available > 0 && available <= settings.LowStockThreshold
                });
            }
        }
        return new InventoryAnalyticsReport
        {
            Rows = rows,
            UnvaluedQuantity = rows.Sum(x => x.UnvaluedQuantity),
            PhysicalUnits = rows.Sum(x => x.Quantity), ReservedUnits = rows.Sum(x => x.ReservedQuantity), AvailableUnits = rows.Sum(x => x.AvailableQuantity),
            InventoryCostUzs = rows.Sum(x => x.InventoryCostUzs), PotentialRevenueUzs = rows.Sum(x => x.PotentialRevenueUzs), PotentialProfitUzs = rows.Sum(x => x.PotentialRevenueUzs - x.InventoryCostUzs),
            StaleInventoryCostUzs = rows.Sum(x => x.StaleCostUzs), LowStockVariants = rows.Count(x => x.IsLowStock)
        };
    }

    private static int SoldSince(IEnumerable<SaleFact> sales, DateTimeOffset since) => Math.Max(0, sales.Where(x => x.CreatedAt >= since).Sum(x => x.Quantity));
    private sealed record SaleFact(DateTimeOffset CreatedAt, int Quantity);
}
