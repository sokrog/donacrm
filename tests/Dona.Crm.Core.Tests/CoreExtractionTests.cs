using System.IO.Compression;
using System.Text.Json;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;

namespace Dona.Crm.Core.Tests;

public sealed class GoogleResourceIdTests
{
    [Theory]
    [InlineData("https://docs.google.com/spreadsheets/d/1Abc_def-234567890/edit", "1Abc_def-234567890")]
    [InlineData("1Abc_def-234567890", "1Abc_def-234567890")]
    [InlineData("not a sheet", "")]
    public void Extracts_existing_spreadsheet_id(string input, string expected) =>
        Assert.Equal(expected, GoogleResourceIds.Spreadsheet(input));

    [Theory]
    [InlineData("https://drive.google.com/drive/folders/1Folder_234567890", "1Folder_234567890")]
    [InlineData("", null)]
    [InlineData("wrong", "")]
    public void Extracts_optional_drive_folder_id(string input, string? expected) =>
        Assert.Equal(expected, GoogleResourceIds.DriveFolder(input));
}

public sealed class GoogleSyncContractTests
{
    [Fact]
    public void Fingerprint_is_stable_when_top_level_record_order_changes()
    {
        var first = new Product { Name = "Первый" };
        var second = new Product { Name = "Второй" };

        var ordered = new DonaSyncSnapshot { Products = [first, second] };
        var reversed = new DonaSyncSnapshot { Products = [second, first] };

        Assert.Equal(DonaSyncFingerprint.Create(ordered), DonaSyncFingerprint.Create(reversed));
    }

    [Fact]
    public void Mapper_preserves_nested_rows_and_omits_removed_interface_mode()
    {
        var product = new Product
        {
            Name = "Dress",
            Variants = [new ProductVariant { Color = "Black", Size = "M", Quantity = 2 }]
        };
        var sale = new Sale
        {
            Number = "SALE-1",
            Payments = [new SalePayment { AmountUzs = 125_000, Status = PaymentStatus.Completed }]
        };

        var sheets = GoogleSyncSheetMapper.Map(new DonaSyncSnapshot { Products = [product], Sales = [sale] });

        Assert.Equal(25, sheets.Count);
        Assert.Equal(sheets.Count, sheets.Select(value => value.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(product.Id.ToString(), sheets.Single(value => value.Title == "ProductVariants").Rows.Single()[1]);
        Assert.Equal(sale.Id.ToString(), sheets.Single(value => value.Title == "Payments").Rows.Single()[1]);
        Assert.DoesNotContain(
            sheets.Single(value => value.Title == "AppSettings").Rows,
            row => string.Equals(row[0]?.ToString(), "SimpleInterfaceMode", StringComparison.Ordinal));
    }
}

public sealed class BackupArchiveCodecTests
{
    [Fact]
    public void Creates_portable_archive_with_snapshot_and_local_images()
    {
        var root = Path.Combine(Path.GetTempPath(), "dona-crm-core-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "product"));
        File.WriteAllBytes(Path.Combine(root, "product", "photo.webp"), [1, 2, 3]);
        try
        {
            var bytes = BackupArchiveCodec.Create(
                new BackupSnapshot { Products = [new Product { Sku = "TS-1", Name = "Test" }] },
                root);
            var inspection = BackupArchiveCodec.Inspect(bytes);

            Assert.Equal("TS-1", Assert.Single(inspection.Snapshot.Products).Sku);
            Assert.Equal(1, inspection.LocalImageCount);
            Assert.Equal(3, inspection.LocalImageBytes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Rejects_unsupported_backup_schema()
    {
        var bytes = BackupArchiveCodec.Create(new BackupSnapshot { SchemaVersion = 99 });

        var exception = Assert.Throws<InvalidOperationException>(() => BackupArchiveCodec.Inspect(bytes));

        Assert.Contains("не поддерживается", exception.Message);
    }

    [Fact]
    public void Rejects_duplicate_archive_paths()
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("dona-crm-backup.json");
            archive.CreateEntry("DONA-CRM-BACKUP.JSON");
        }

        var exception = Assert.Throws<InvalidOperationException>(() => BackupArchiveCodec.Inspect(output.ToArray()));

        Assert.Contains("повторяющиеся пути", exception.Message);
    }
}

public sealed class BusinessSettingsCompatibilityTests
{
    [Fact]
    public void Legacy_simple_interface_property_is_ignored_when_reading_settings()
    {
        const string legacyJson = """
            {
              "SimpleInterfaceMode": true,
              "LowStockThreshold": 7,
              "MainCurrencyCode": "UZS"
            }
            """;

        var settings = JsonSerializer.Deserialize<BusinessSettings>(legacyJson);

        Assert.NotNull(settings);
        Assert.Equal(7, settings.LowStockThreshold);
        Assert.Equal("UZS", settings.MainCurrencyCode);
        Assert.DoesNotContain("SimpleInterfaceMode", JsonSerializer.Serialize(settings));
    }
}
