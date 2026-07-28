using System.IO.Compression;
using System.Text.Json;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Web.Services;

public sealed record BackupDownload(byte[] Content, string FileName, string ContentType = "application/zip");
public sealed record BackupArchiveInspection(BackupSnapshot Snapshot, int LocalImageCount, long LocalImageBytes);
public sealed record BackupPreview(DateTimeOffset CreatedAt, int SchemaVersion, int Products, int Purchases, int Sales, int Customers, int Suppliers, int Intermediaries, int Categories, int Collections, int Outfits, int ContentItems, int StockMovements, int LocalImages, long LocalImageBytes, int NewProducts, int UpdatedProducts, int NewPurchases, int UpdatedPurchases, int NewSales, int UpdatedSales);
public sealed record BackupRestoreResult(BackupDownload AutomaticBackup, BackupPreview Preview);

public interface IBackupSnapshotStore
{
    Task<DonaSyncSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default);
    Task ReplaceSnapshotAsync(DonaSyncSnapshot snapshot, CancellationToken cancellationToken = default);
}

public interface IBackupArchiveFileService
{
    Task<byte[]?> PickAsync(CancellationToken cancellationToken = default);
    Task SaveAutomaticBackupAsync(BackupDownload backup, CancellationToken cancellationToken = default);
    Task ExportAsync(BackupDownload backup, CancellationToken cancellationToken = default);
}

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

public static class BackupSnapshotMapper
{
    public static BackupSnapshot FromSyncSnapshot(DonaSyncSnapshot snapshot) => new()
    {
        Products = snapshot.Products,
        Commerce = new CommerceData
        {
            Suppliers = snapshot.Suppliers,
            Intermediaries = snapshot.Intermediaries,
            Categories = snapshot.Categories,
            Purchases = snapshot.Purchases
        },
        Sales = new SalesData { Customers = snapshot.Customers, Sales = snapshot.Sales },
        Marketing = snapshot.Marketing,
        PurchaseHistory = snapshot.PurchaseHistory,
        StockMovements = snapshot.StockMovements,
        BusinessSettings = snapshot.BusinessSettings
    };

    public static DonaSyncSnapshot ToSyncSnapshot(BackupSnapshot snapshot) => new()
    {
        Products = snapshot.Products.ToList(),
        Suppliers = snapshot.Commerce.Suppliers.ToList(),
        Intermediaries = snapshot.Commerce.Intermediaries.ToList(),
        Categories = snapshot.Commerce.Categories.ToList(),
        Purchases = snapshot.Commerce.Purchases.ToList(),
        Customers = snapshot.Sales.Customers.ToList(),
        Sales = snapshot.Sales.Sales.ToList(),
        Marketing = snapshot.Marketing,
        PurchaseHistory = snapshot.PurchaseHistory,
        StockMovements = snapshot.StockMovements.ToList(),
        BusinessSettings = snapshot.BusinessSettings
    };
}

public sealed class BackupRestoreService(IBackupSnapshotStore store)
{
    public async Task<BackupPreview> PreviewAsync(byte[] content, CancellationToken cancellationToken = default)
    {
        var inspection = BackupArchiveCodec.Inspect(content);
        var current = await store.ReadSnapshotAsync(cancellationToken);
        var incoming = BackupSnapshotMapper.ToSyncSnapshot(inspection.Snapshot);
        return CreatePreview(inspection, current, incoming);
    }

    public async Task<BackupRestoreResult> RestoreAsync(byte[] content, CancellationToken cancellationToken = default)
    {
        var inspection = BackupArchiveCodec.Inspect(content);
        var current = await store.ReadSnapshotAsync(cancellationToken);
        var incoming = BackupSnapshotMapper.ToSyncSnapshot(inspection.Snapshot);
        var preview = CreatePreview(inspection, current, incoming);
        var automaticBackup = new BackupDownload(
            BackupArchiveCodec.Create(BackupSnapshotMapper.FromSyncSnapshot(current)),
            $"dona-crm-before-restore-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip");
        await store.ReplaceSnapshotAsync(incoming, cancellationToken);
        return new BackupRestoreResult(automaticBackup, preview);
    }

    private static BackupPreview CreatePreview(BackupArchiveInspection inspection, DonaSyncSnapshot current, DonaSyncSnapshot incoming) => new(
        inspection.Snapshot.CreatedAt, inspection.Snapshot.SchemaVersion, incoming.Products.Count, incoming.Purchases.Count, incoming.Sales.Count,
        incoming.Customers.Count, incoming.Suppliers.Count, incoming.Intermediaries.Count, incoming.Categories.Count,
        incoming.Marketing.Collections.Count, incoming.Marketing.Outfits.Count, incoming.Marketing.ContentPosts.Count,
        incoming.StockMovements.Count, inspection.LocalImageCount, inspection.LocalImageBytes,
        NewCount(current.Products, incoming.Products), UpdatedCount(current.Products, incoming.Products),
        NewCount(current.Purchases, incoming.Purchases), UpdatedCount(current.Purchases, incoming.Purchases),
        NewCount(current.Sales, incoming.Sales), UpdatedCount(current.Sales, incoming.Sales));

    private static int NewCount<T>(IEnumerable<T> current, IEnumerable<T> incoming) where T : class =>
        incoming.Count(item => !Ids(current).Contains(Id(item)));
    private static int UpdatedCount<T>(IEnumerable<T> current, IEnumerable<T> incoming) where T : class =>
        incoming.Count(item => Ids(current).Contains(Id(item)));
    private static HashSet<Guid> Ids<T>(IEnumerable<T> values) where T : class => values.Select(Id).ToHashSet();
    private static Guid Id<T>(T value) where T : class => (Guid)(value.GetType().GetProperty("Id")?.GetValue(value) ?? Guid.Empty);
}

public static class BackupArchiveCodec
{
    private const long MaxJsonSize = 50 * 1024 * 1024;
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true, MaxDepth = 64 };

    public static byte[] Create(BackupSnapshot snapshot, string? localImagesPath = null)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var dataEntry = archive.CreateEntry("dona-crm-backup.json", CompressionLevel.Optimal);
            using (var stream = dataEntry.Open()) JsonSerializer.Serialize(stream, snapshot, WriteOptions);

            var readme = archive.CreateEntry("README.txt", CompressionLevel.Optimal);
            using (var writer = new StreamWriter(readme.Open()))
                writer.Write("Dona CRM backup. Credentials and OAuth tokens are intentionally excluded. Google Drive images remain in Drive; local product images are included in the images folder.");

            if (!string.IsNullOrWhiteSpace(localImagesPath) && Directory.Exists(localImagesPath))
            {
                foreach (var file in Directory.EnumerateFiles(localImagesPath, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(localImagesPath, file).Replace('\\', '/');
                    archive.CreateEntryFromFile(file, $"images/{relative}", CompressionLevel.Optimal);
                }
            }
        }

        return output.ToArray();
    }

    public static BackupDownload CreateJsonExport(BackupSnapshot snapshot) => new(
        JsonSerializer.SerializeToUtf8Bytes(snapshot, WriteOptions),
        $"dona-crm-data-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json",
        "application/json");

    public static BackupArchiveInspection Inspect(byte[] content)
    {
        if (content.Length == 0) throw new InvalidOperationException("Архив пуст.");
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            if (archive.Entries.Count > 10_000) throw new InvalidOperationException("В архиве слишком много файлов.");
            if (archive.Entries.GroupBy(x => x.FullName, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
                throw new InvalidOperationException("В архиве есть повторяющиеся пути.");

            var dataEntries = archive.Entries
                .Where(x => x.FullName.Equals("dona-crm-backup.json", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (dataEntries.Count != 1) throw new InvalidOperationException("Файл dona-crm-backup.json не найден или повторяется.");

            var data = dataEntries[0];
            if (data.Length <= 0 || data.Length > MaxJsonSize) throw new InvalidOperationException("Некорректный размер файла данных.");
            using var json = data.Open();
            var snapshot = JsonSerializer.Deserialize<BackupSnapshot>(json, ReadOptions)
                ?? throw new InvalidOperationException("Файл данных пуст или повреждён.");
            if (snapshot.SchemaVersion != 1) throw new InvalidOperationException($"Версия схемы {snapshot.SchemaVersion} не поддерживается.");

            var images = archive.Entries
                .Where(x => x.FullName.StartsWith("images/", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(x.Name))
                .ToList();
            if (images.Any(x => x.FullName.Split('/').Any(part => part is ".." or ".")))
                throw new InvalidOperationException("В архиве найден небезопасный путь изображения.");

            return new BackupArchiveInspection(snapshot, images.Count, images.Sum(x => x.Length));
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidOperationException("Файл не является корректным ZIP-архивом.", exception);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("JSON резервной копии повреждён.", exception);
        }
    }
}
