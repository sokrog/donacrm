using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Web.Services;

public sealed class HomeDashboardService(
    ICatalogRepository catalog,
    ICommerceRepository commerce,
    ISalesRepository sales,
    IBusinessSettingsRepository settings,
    IStockMovementRepository stockMovements)
{
    public async Task<HomeDashboardSnapshot> LoadAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var productsTask = catalog.GetProductsAsync(cancellationToken);
        var purchasesTask = commerce.GetPurchasesAsync(cancellationToken);
        var salesTask = sales.GetSalesAsync(cancellationToken);
        var settingsTask = settings.GetAsync(cancellationToken);
        var movementsTask = stockMovements.GetAsync(cancellationToken);
        await Task.WhenAll(productsTask, purchasesTask, salesTask, settingsTask, movementsTask);

        return Calculate(
            await productsTask,
            await purchasesTask,
            await salesTask,
            await settingsTask,
            await movementsTask,
            now);
    }

    public static HomeDashboardSnapshot Calculate(
        IReadOnlyList<Product> products,
        IReadOnlyList<Purchase> purchases,
        IReadOnlyList<Sale> sales,
        BusinessSettings settings,
        IReadOnlyList<StockMovement> movements,
        DateTimeOffset now)
    {
        var localDate = now.LocalDateTime.Date;
        var activeSales = sales.Where(item => item.Status != SaleStatus.Cancelled).ToList();
        var todaySales = activeSales.Where(item => item.CreatedAt.LocalDateTime.Date == localDate).ToList();
        var incoming = purchases
            .Where(item => item.Status is PurchaseStatus.Ordered or PurchaseStatus.ChinaWarehouse or PurchaseStatus.Shipped or PurchaseStatus.PartiallyReceived)
            .ToList();

        return new HomeDashboardSnapshot
        {
            TodayRevenueUzs = todaySales.Sum(item => item.NetTotalUzs),
            TodayProfitUzs = todaySales.Sum(item => item.ProfitUzs),
            TodaySalesCount = todaySales.Count,
            CustomerDebtUzs = activeSales.Sum(item => item.BalanceDueUzs),
            AvailableQuantity = products.Sum(item => item.Variants.Sum(variant => variant.AvailableQuantity)),
            IncomingQuantity = incoming.Sum(item => item.Items.Sum(line => line.MissingQuantity)),
            LowStockProducts = products
                .Where(item => item.Status is ProductStatus.InStock or ProductStatus.LowStock && item.Quantity <= settings.LowStockThreshold)
                .OrderBy(item => item.Quantity)
                .ToList(),
            SalesWithDebt = activeSales.Where(item => item.BalanceDueUzs > 0).OrderByDescending(item => item.BalanceDueUzs).ToList(),
            DuePurchases = incoming
                .Where(item => item.EstimatedDeliveryDate is not null && item.EstimatedDeliveryDate.Value.Date <= localDate)
                .OrderBy(item => item.EstimatedDeliveryDate)
                .ToList(),
            ProductsWithoutImages = products.Where(item => string.IsNullOrWhiteSpace(item.PrimaryImageUrl)).ToList(),
            RecentSales = activeSales.OrderByDescending(item => item.CreatedAt).Take(8).ToList(),
            RecentMovements = movements.OrderByDescending(item => item.CreatedAt).Take(8).ToList()
        };
    }
}

public sealed class HomeDashboardSnapshot
{
    public decimal TodayRevenueUzs { get; init; }
    public decimal TodayProfitUzs { get; init; }
    public decimal CustomerDebtUzs { get; init; }
    public int TodaySalesCount { get; init; }
    public int AvailableQuantity { get; init; }
    public int IncomingQuantity { get; init; }
    public IReadOnlyList<Product> LowStockProducts { get; init; } = [];
    public IReadOnlyList<Sale> SalesWithDebt { get; init; } = [];
    public IReadOnlyList<Purchase> DuePurchases { get; init; } = [];
    public IReadOnlyList<Product> ProductsWithoutImages { get; init; } = [];
    public IReadOnlyList<Sale> RecentSales { get; init; } = [];
    public IReadOnlyList<StockMovement> RecentMovements { get; init; } = [];
}
