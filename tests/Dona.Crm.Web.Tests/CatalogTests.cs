using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace Dona.Crm.Web.Tests;

public sealed class ProductEconomicsTests
{
    [Fact]
    public void Calculates_cost_profit_and_markup()
    {
        var product = new Product
        {
            PurchasePriceCny = 28,
            CnyRateUzs = 1_800,
            AgentCommissionPercent = 5,
            DeliveryCostUzs = 18_000,
            SellingPriceUzs = 119_000
        };

        Assert.Equal(70_920, product.CostUzs);
        Assert.Equal(48_080, product.ProfitUzs);
        Assert.Equal(67.8m, product.MarkupPercent);
    }

    [Fact]
    public void Calculates_stock_from_variants()
    {
        var product = new Product
        {
            Variants =
            [
                new ProductVariant { Quantity = 3, ReservedQuantity = 1 },
                new ProductVariant { Quantity = 5, ReservedQuantity = 2 }
            ]
        };

        Assert.Equal(8, product.Quantity);
        Assert.Equal(2, product.Variants[0].AvailableQuantity);
        Assert.Equal(3, product.Variants[1].AvailableQuantity);
    }
}

public sealed class JsonCatalogRepositoryTests
{
    [Fact]
    public async Task Persists_a_product_and_reads_it_with_a_new_repository_instance()
    {
        var root = Path.Combine(Path.GetTempPath(), "dona-crm-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var environment = new TestEnvironment(root);
            var options = Options.Create(new StorageOptions { DataFile = "data/catalog.json" });
            var firstRepository = new JsonCatalogRepository(environment, options);
            var product = new Product { Sku = "TEST-001", Name = "Тестовый товар", SellingPriceUzs = 99_000 };

            await firstRepository.UpsertProductAsync(product);

            var secondRepository = new JsonCatalogRepository(environment, options);
            var restored = await secondRepository.GetProductAsync(product.Id);
            Assert.NotNull(restored);
            Assert.Equal("TEST-001", restored.Sku);
            Assert.Equal(99_000, restored.SellingPriceUzs);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TestEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Dona.Crm.Web.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

public sealed class GoogleSheetsSettingsStoreTests
{
    [Fact]
    public async Task Saves_url_as_spreadsheet_id_and_persists_credentials()
    {
        var root = Path.Combine(Path.GetTempPath(), "dona-crm-settings-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var environment = new SettingsEnvironment(root);
            var options = Options.Create(new GoogleSheetsOptions());
            var store = new GoogleSheetsSettingsStore(environment, options);
            const string id = "1AbCdEfGhIjKlMnOpQrStUvWxYz";
            const string credentials = """{"type":"service_account","client_email":"catalog@test.iam.gserviceaccount.com","private_key":"secret"}""";

            await store.SaveAsync($"https://docs.google.com/spreadsheets/d/{id}/edit", credentials);

            Assert.Equal(id, store.SpreadsheetId);
            Assert.Equal("catalog@test.iam.gserviceaccount.com", store.ServiceAccountEmail);
            Assert.True(store.IsConfigured);
            var reloaded = new GoogleSheetsSettingsStore(environment, options);
            Assert.Equal(id, reloaded.SpreadsheetId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class SettingsEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Dona.Crm.Web.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
