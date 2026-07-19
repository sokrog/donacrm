using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;
using System.IO.Compression;
using System.Text.Json;

namespace Dona.Crm.Web.Services;

public sealed record BackupDownload(byte[] Content, string FileName);

public sealed class BackupSnapshot
{
    public int SchemaVersion { get; init; } = 1;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<Product> Products { get; init; } = [];
    public CommerceData Commerce { get; init; } = new();
    public SalesData Sales { get; init; } = new();
    public MarketingData Marketing { get; init; } = new();
    public PurchaseHistoryData PurchaseHistory { get; init; } = new();
    public IReadOnlyList<StockMovement> StockMovements { get; init; } = [];
    public BusinessSettings BusinessSettings { get; init; } = new();
}

public sealed class BackupService(ICatalogRepository catalog, ICommerceRepository commerce, ISalesRepository sales, IMarketingRepository marketing, IPurchaseHistoryRepository purchaseHistory, IStockMovementRepository stockMovements, IBusinessSettingsRepository settings, IWebHostEnvironment environment)
{
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

    public static byte[] CreateArchive(BackupSnapshot snapshot, string localImagesPath)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var dataEntry = archive.CreateEntry("dona-crm-backup.json", CompressionLevel.Optimal);
            using (var stream = dataEntry.Open()) JsonSerializer.Serialize(stream, snapshot, new JsonSerializerOptions { WriteIndented = true });
            var readme = archive.CreateEntry("README.txt", CompressionLevel.Optimal);
            using (var writer = new StreamWriter(readme.Open())) writer.Write("Dona CRM backup. Credentials and OAuth tokens are intentionally excluded. Google Drive images remain in Drive; local product images are included in the images folder.");
            if (Directory.Exists(localImagesPath))
                foreach (var file in Directory.EnumerateFiles(localImagesPath, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(localImagesPath, file).Replace('\\', '/');
                    archive.CreateEntryFromFile(file, $"images/{relative}", CompressionLevel.Optimal);
                }
        }
        return output.ToArray();
    }
}
