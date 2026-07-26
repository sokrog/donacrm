using System.IO.Compression;
using System.Text.Json;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;

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
