using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;
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
        var collection = new ProductCollection
        {
            Name = "Summer",
            Images = [new ProductImage { FileName = "collection.webp", IsMain = true }]
        };
        var outfit = new Outfit
        {
            Name = "Evening",
            Images = [new ProductImage { FileName = "outfit.webp", IsMain = true }]
        };

        var sheets = GoogleSyncSheetMapper.Map(new DonaSyncSnapshot
        {
            Products = [product],
            Sales = [sale],
            Marketing = new MarketingData { Collections = [collection], Outfits = [outfit] }
        });

        Assert.Equal(27, sheets.Count);
        Assert.Equal(sheets.Count, sheets.Select(value => value.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(product.Id.ToString(), sheets.Single(value => value.Title == "ProductVariants").Rows.Single()[1]);
        Assert.Equal(sale.Id.ToString(), sheets.Single(value => value.Title == "Payments").Rows.Single()[1]);
        Assert.Equal(collection.Id.ToString(), sheets.Single(value => value.Title == "CollectionImages").Rows.Single()[1]);
        Assert.Equal(outfit.Id.ToString(), sheets.Single(value => value.Title == "OutfitImages").Rows.Single()[1]);
        Assert.DoesNotContain(
            sheets.Single(value => value.Title == "AppSettings").Rows,
            row => string.Equals(row[0]?.ToString(), "SimpleInterfaceMode", StringComparison.Ordinal));
    }

    [Fact]
    public void Visible_sheet_parser_restores_nested_business_data()
    {
        var product = new Product
        {
            Sku = "DR-1",
            Name = "Dress",
            Variants = [new ProductVariant { Color = "Black", Size = "M", Quantity = 2 }],
            Images = [new ProductImage { FileName = "dress.webp", Url = "https://example.test/dress.webp", IsMain = true }]
        };
        var purchase = new Purchase
        {
            Number = "PO-1",
            Items = [new PurchaseItem { ProductId = product.Id, ProductName = product.Name, Quantity = 2 }],
            Receipts =
            [
                new PurchaseReceipt
                {
                    Lines =
                    [
                        new PurchaseReceiptLine
                        {
                            PurchaseItemId = Guid.Empty,
                            ProductId = product.Id,
                            ProductName = product.Name,
                            ReceivedQuantity = 2,
                            StockedQuantity = 2
                        }
                    ]
                }
            ]
        };
        purchase.Receipts[0].Lines[0].PurchaseItemId = purchase.Items[0].Id;
        var sale = new Sale
        {
            Number = "SALE-1",
            Items = [new SaleItem { ProductId = product.Id, ProductName = product.Name, Quantity = 1 }],
            Payments = [new SalePayment { AmountUzs = 125_000, Status = PaymentStatus.Completed }]
        };
        var snapshot = new DonaSyncSnapshot
        {
            Products = [product],
            Purchases = [purchase],
            Sales = [sale],
            BusinessSettings = new BusinessSettings { LowStockThreshold = 9 }
        };

        var result = GoogleSheetSnapshotParser.Parse(GoogleSyncSheetMapper.Map(snapshot));

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Issues));
        Assert.Single(Assert.Single(result.Snapshot.Products).Variants);
        Assert.Single(result.Snapshot.Products[0].Images);
        Assert.Single(Assert.Single(result.Snapshot.Purchases).Items);
        Assert.Single(result.Snapshot.Purchases[0].Receipts[0].Lines);
        Assert.Single(Assert.Single(result.Snapshot.Sales).Payments);
        Assert.Equal(9, result.Snapshot.BusinessSettings.LowStockThreshold);
        Assert.Equal("DR-1", result.Snapshot.Products[0].Sku);
        Assert.Equal(125_000, result.Snapshot.Sales[0].Payments[0].AmountUzs);
    }

    [Fact]
    public void Visible_sheet_parser_matches_columns_by_header_name()
    {
        var id = Guid.NewGuid();
        var sheet = new GoogleSyncSheet(
            "Products",
            ["Name", "UnknownColumn", "Id", "Sku"],
            [new object[] { "Dress", "ignored", id.ToString(), "DR-1" }]);

        var result = GoogleSheetSnapshotParser.Parse([sheet]);

        Assert.True(result.IsValid);
        var product = Assert.Single(result.Snapshot.Products);
        Assert.Equal(id, product.Id);
        Assert.Equal("DR-1", product.Sku);
        Assert.Equal("Dress", product.Name);
    }

    [Fact]
    public void Visible_sheet_parser_reports_orphaned_child_rows()
    {
        var sheet = new GoogleSyncSheet(
            "ProductVariants",
            ["Id", "ProductId", "Color"],
            [new object[] { Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "Black" }]);

        var result = GoogleSheetSnapshotParser.Parse([sheet]);

        var issue = Assert.Single(result.Issues);
        Assert.Equal("ProductVariants", issue.Sheet);
        Assert.Equal(2, issue.Row);
        Assert.Contains("ProductId", issue.Message);
    }

    [Fact]
    public void Visible_sheet_parser_ignores_removed_legacy_setting()
    {
        var sheet = new GoogleSyncSheet(
            "AppSettings",
            ["Key", "Value"],
            [
                new object[] { "SimpleInterfaceMode", true },
                new object[] { nameof(BusinessSettings.LowStockThreshold), 6 }
            ]);

        var result = GoogleSheetSnapshotParser.Parse([sheet]);

        Assert.True(result.IsValid);
        Assert.Equal(6, result.Snapshot.BusinessSettings.LowStockThreshold);
    }

    [Fact]
    public async Task Sheets_client_reads_portable_hidden_snapshot()
    {
        var snapshot = new DonaSyncSnapshot
        {
            Products = [new Product { Sku = "TS-1", Name = "T-shirt" }]
        };
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot)));
        var handler = new StubHttpHandler(_ => JsonResponse(
            $$"""{"values":[["CommandOrbitSyncV1","version-1","2026-07-26T10:00:00+05:00"],["1","{{encoded}}"]]}"""));
        var client = new GoogleSheetsSnapshotClient(new HttpClient(handler));

        var envelope = await client.ReadAsync("spreadsheet-id", "access-token");

        Assert.Equal("version-1", envelope.Version);
        Assert.Equal("TS-1", Assert.Single(envelope.Snapshot.Products).Sku);
        Assert.Equal("Bearer access-token", Assert.Single(handler.Requests).Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task Sheets_client_falls_back_to_existing_visible_sheets()
    {
        var productId = Guid.NewGuid();
        var responses = new Queue<HttpResponseMessage>(
        [
            new(HttpStatusCode.BadRequest),
            JsonResponse("""{"sheets":[{"properties":{"title":"Products"}}]}"""),
            JsonResponse($$"""{"valueRanges":[{"values":[["Id","Sku","Name"],["{{productId}}","TS-2","Existing"]]}]}""")
        ]);
        var client = new GoogleSheetsSnapshotClient(new HttpClient(new StubHttpHandler(_ => responses.Dequeue())));

        var envelope = await client.ReadAsync("spreadsheet-id", "access-token");

        var product = Assert.Single(envelope.Snapshot.Products);
        Assert.Equal(productId, product.Id);
        Assert.Equal("Existing", product.Name);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(response(request));
        }
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
