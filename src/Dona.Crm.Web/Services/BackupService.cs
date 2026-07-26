using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Web.Services;

public sealed class BackupService(ICatalogRepository catalog, ICommerceRepository commerce, ISalesRepository sales, IMarketingRepository marketing, IPurchaseHistoryRepository purchaseHistory, IStockMovementRepository stockMovements, IBusinessSettingsRepository settings, IWebHostEnvironment environment)
{
    private const long MaxBackupSize = 100 * 1024 * 1024;
    public async Task<BackupDownload> CreateAsync(CancellationToken token = default)
    {
        var snapshot = new BackupSnapshot
        {
            Products = await catalog.GetProductsAsync(token),
            Commerce = new CommerceData { Suppliers = (await commerce.GetSuppliersAsync(token)).ToList(), Intermediaries = (await commerce.GetIntermediariesAsync(token)).ToList(), Categories = (await commerce.GetCategoriesAsync(token)).ToList(), Purchases = (await commerce.GetPurchasesAsync(token)).ToList() },
            Sales = new SalesData { Customers = (await sales.GetCustomersAsync(token)).ToList(), Sales = (await sales.GetSalesAsync(token)).ToList() },
            Marketing = await marketing.GetDataAsync(token),
            PurchaseHistory = await purchaseHistory.GetAsync(token),
            StockMovements = await stockMovements.GetAsync(token),
            BusinessSettings = await settings.GetAsync(token)
        };
        var localImages = Path.Combine(environment.WebRootPath, "uploads", "products");
        return new BackupDownload(CreateArchive(snapshot, localImages), $"dona-crm-backup-{DateTime.Now:yyyy-MM-dd-HHmm}.zip");
    }

    public static byte[] CreateArchive(BackupSnapshot snapshot, string localImagesPath) =>
        BackupArchiveCodec.Create(snapshot, localImagesPath);

    public async Task<BackupPreview> InspectAsync(Stream source, CancellationToken token = default)
    {
        await using var buffer = new MemoryStream();
        var chunk = new byte[81920]; long total = 0;
        while (true) { var read = await source.ReadAsync(chunk, token); if (read == 0) break; total += read; if (total > MaxBackupSize) throw new InvalidOperationException("Резервная копия больше 100 МБ."); await buffer.WriteAsync(chunk.AsMemory(0, read), token); }
        var inspection = InspectArchive(buffer.ToArray()); var snapshot = inspection.Snapshot;
        var currentProducts = await catalog.GetProductsAsync(token); var currentPurchases = await commerce.GetPurchasesAsync(token); var currentSales = await sales.GetSalesAsync(token);
        var productIds = currentProducts.Select(x => x.Id).ToHashSet(); var purchaseIds = currentPurchases.Select(x => x.Id).ToHashSet(); var saleIds = currentSales.Select(x => x.Id).ToHashSet();
        return new BackupPreview(snapshot.CreatedAt, snapshot.SchemaVersion, snapshot.Products.Count, snapshot.Commerce.Purchases.Count, snapshot.Sales.Sales.Count, snapshot.Sales.Customers.Count, snapshot.Commerce.Suppliers.Count, snapshot.Commerce.Intermediaries.Count, snapshot.Commerce.Categories.Count, snapshot.Marketing.Collections.Count, snapshot.Marketing.Outfits.Count, snapshot.Marketing.ContentPosts.Count, snapshot.StockMovements.Count, inspection.LocalImageCount, inspection.LocalImageBytes, snapshot.Products.Count(x => !productIds.Contains(x.Id)), snapshot.Products.Count(x => productIds.Contains(x.Id)), snapshot.Commerce.Purchases.Count(x => !purchaseIds.Contains(x.Id)), snapshot.Commerce.Purchases.Count(x => purchaseIds.Contains(x.Id)), snapshot.Sales.Sales.Count(x => !saleIds.Contains(x.Id)), snapshot.Sales.Sales.Count(x => saleIds.Contains(x.Id)));
    }

    public static BackupArchiveInspection InspectArchive(byte[] content) => BackupArchiveCodec.Inspect(content);
}
