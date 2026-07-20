using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;
using System.IO.Compression;
using System.Text.Json;

namespace Dona.Crm.Web.Services;

public sealed record BackupDownload(byte[] Content, string FileName);
public sealed record BackupArchiveInspection(BackupSnapshot Snapshot, int LocalImageCount, long LocalImageBytes);
public sealed record BackupPreview(DateTimeOffset CreatedAt, int SchemaVersion, int Products, int Purchases, int Sales, int Customers, int Suppliers, int Intermediaries, int Categories, int Collections, int Outfits, int ContentItems, int StockMovements, int LocalImages, long LocalImageBytes, int NewProducts, int UpdatedProducts, int NewPurchases, int UpdatedPurchases, int NewSales, int UpdatedSales);

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
    private const long MaxBackupSize = 100 * 1024 * 1024;
    private const long MaxJsonSize = 50 * 1024 * 1024;
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

    public static BackupArchiveInspection InspectArchive(byte[] content)
    {
        if (content.Length == 0) throw new InvalidOperationException("Архив пуст.");
        try
        {
            using var stream = new MemoryStream(content, writable: false); using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            if (archive.Entries.Count > 10_000) throw new InvalidOperationException("В архиве слишком много файлов.");
            if (archive.Entries.GroupBy(x => x.FullName, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1)) throw new InvalidOperationException("В архиве есть повторяющиеся пути.");
            var dataEntries = archive.Entries.Where(x => x.FullName.Equals("dona-crm-backup.json", StringComparison.OrdinalIgnoreCase)).ToList();
            if (dataEntries.Count != 1) throw new InvalidOperationException("Файл dona-crm-backup.json не найден или повторяется.");
            var data = dataEntries[0]; if (data.Length <= 0 || data.Length > MaxJsonSize) throw new InvalidOperationException("Некорректный размер файла данных.");
            using var json = data.Open();
            var snapshot = JsonSerializer.Deserialize<BackupSnapshot>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, MaxDepth = 64 }) ?? throw new InvalidOperationException("Файл данных пуст или повреждён.");
            if (snapshot.SchemaVersion != 1) throw new InvalidOperationException($"Версия схемы {snapshot.SchemaVersion} не поддерживается.");
            var images = archive.Entries.Where(x => x.FullName.StartsWith("images/", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(x.Name)).ToList();
            if (images.Any(x => x.FullName.Split('/').Any(part => part is ".." or "."))) throw new InvalidOperationException("В архиве найден небезопасный путь изображения.");
            return new BackupArchiveInspection(snapshot, images.Count, images.Sum(x => x.Length));
        }
        catch (InvalidDataException exception) { throw new InvalidOperationException("Файл не является корректным ZIP-архивом.", exception); }
        catch (JsonException exception) { throw new InvalidOperationException("JSON резервной копии повреждён.", exception); }
    }
}
