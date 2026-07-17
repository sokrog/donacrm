using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;
using Dona.Crm.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace Dona.Crm.Web.Tests;

public sealed class ProductEconomicsTests
{
    [Fact]
    public void New_product_has_no_prefilled_form_values()
    {
        var product = new Product();
        Assert.Empty(product.Name);
        Assert.Empty(product.Category);
        Assert.Null(product.Status);
        Assert.Null(product.CnyRateUzs);
        Assert.Null(product.SellingPriceUzs);
    }

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

public sealed class PurchaseEconomicsTests
{
    [Fact]
    public void Calculates_complete_purchase_cost()
    {
        var purchase = new Purchase
        {
            CnyRateUzs = 1_800,
            AgentCommissionPercent = 5,
            InternationalShippingUzs = 100_000,
            OtherCostsUzs = 20_000,
            Items = [new PurchaseItem { ProductName = "Футболка", Quantity = 10, UnitPriceCny = 25 }]
        };

        Assert.Equal(250, purchase.GoodsCostCny);
        Assert.Equal(450_000, purchase.GoodsCostUzs);
        Assert.Equal(22_500, purchase.AgentCommissionUzs);
        Assert.Equal(592_500, purchase.TotalCostUzs);
        Assert.Equal(10, purchase.TotalQuantity);
    }

    [Fact]
    public void Allocates_shipping_by_weight_and_calculates_receipt_quantities()
    {
        var light = new PurchaseItem { ProductName = "Лёгкий", Quantity = 10, UnitPriceCny = 10, UnitWeightKg = 0.1m, ReceivedQuantity = 9, DefectQuantity = 1 };
        var heavy = new PurchaseItem { ProductName = "Тяжёлый", Quantity = 10, UnitPriceCny = 10, UnitWeightKg = 0.3m };
        var purchase = new Purchase { CnyRateUzs = 1_000, InternationalShippingUzs = 40_000, Items = [light, heavy] };

        Assert.Equal(10_000, purchase.ItemShippingUzs(light));
        Assert.Equal(30_000, purchase.ItemShippingUzs(heavy));
        Assert.Equal(1, light.MissingQuantity);
        Assert.Equal(8, light.AcceptedQuantity);
        Assert.Equal(8, light.QuantityToStock);
    }
}

public sealed class PurchaseReceivingServiceTests
{
    [Fact]
    public async Task Adds_only_accepted_quantity_and_does_not_add_it_twice()
    {
        var variant = new ProductVariant { Color = "Черный", Size = "M", Quantity = 2 };
        var product = new Product { Name = "Футболка", Sku = "TS-1", Variants = [variant] };
        var item = new PurchaseItem { ProductId = product.Id, ProductVariantId = variant.Id, ProductName = product.Name, Color = variant.Color, Size = variant.Size, Quantity = 10, ReceivedQuantity = 8, DefectQuantity = 1, UnitPriceCny = 20 };
        var purchase = new Purchase { Items = [item] };
        var catalog = new MemoryCatalog(product);
        var commerce = new MemoryCommerce();
        var service = new PurchaseReceivingService(catalog, commerce);

        var first = await service.ReceiveAsync(purchase);
        var second = await service.ReceiveAsync(purchase);

        Assert.Equal(7, first.AddedUnits);
        Assert.Equal(0, second.AddedUnits);
        Assert.Equal(9, variant.Quantity);
        Assert.Equal(7, item.StockedQuantity);
        Assert.Equal(PurchaseStatus.Received, purchase.Status);
    }

    private sealed class MemoryCatalog(Product product) : ICatalogRepository
    {
        public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Product>>([product]);
        public Task<Product?> GetProductAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Product?>(id == product.Id ? product : null);
        public Task UpsertProductAsync(Product value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteProductAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class MemoryCommerce : ICommerceRepository
    {
        public Task<IReadOnlyList<Supplier>> GetSuppliersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Supplier>>([]);
        public Task UpsertSupplierAsync(Supplier supplier, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteSupplierAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Intermediary>> GetIntermediariesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Intermediary>>([]);
        public Task UpsertIntermediaryAsync(Intermediary intermediary, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteIntermediaryAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Category>>([]);
        public Task UpsertCategoryAsync(Category category, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Purchase>> GetPurchasesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Purchase>>([]);
        public Task<Purchase?> GetPurchaseAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Purchase?>(null);
        public Task UpsertPurchaseAsync(Purchase purchase, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
            await secondRepository.DeleteProductAsync(product.Id);
            Assert.Null(await secondRepository.GetProductAsync(product.Id));
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

public sealed class SalesInventoryServiceTests
{
    [Fact]
    public async Task Reserve_complete_and_return_are_idempotent()
    {
        var variant = new ProductVariant { Color = "Черный", Size = "M", Quantity = 10 };
        var product = new Product { Name = "Футболка", Sku = "TS-2", SellingPriceUzs = 120_000, Variants = [variant] };
        var item = new SaleItem { ProductId = product.Id, ProductVariantId = variant.Id, ProductName = product.Name, Color = variant.Color, Size = variant.Size, Quantity = 3, UnitPriceUzs = 120_000 };
        var sale = new Sale { Number = "SALE-1", Items = [item] };
        var catalog = new SalesMemoryCatalog(product);
        var sales = new MemorySales();
        var service = new SalesInventoryService(catalog, sales);

        await service.ReserveAsync(sale);
        await service.ReserveAsync(sale);
        Assert.Equal(3, variant.ReservedQuantity);
        Assert.Equal(3, item.ReservedQuantity);

        await service.CompleteAsync(sale);
        await service.CompleteAsync(sale);
        Assert.Equal(7, variant.Quantity);
        Assert.Equal(0, variant.ReservedQuantity);
        Assert.Equal(3, item.SoldQuantity);
        Assert.Equal(SaleStatus.Completed, sale.Status);

        await service.ReturnAsync(sale);
        await service.ReturnAsync(sale);
        Assert.Equal(10, variant.Quantity);
        Assert.Equal(3, item.ReturnedQuantity);
        Assert.Equal(SaleStatus.Returned, sale.Status);
    }

    [Fact]
    public async Task Cancelling_releases_reservation_without_changing_stock()
    {
        var variant = new ProductVariant { Quantity = 5 };
        var product = new Product { Name = "Сумка", Sku = "BG-2", Variants = [variant] };
        var sale = new Sale { Number = "SALE-2", Items = [new SaleItem { ProductId = product.Id, ProductVariantId = variant.Id, ProductName = product.Name, Quantity = 2 }] };
        var service = new SalesInventoryService(new SalesMemoryCatalog(product), new MemorySales());

        await service.ReserveAsync(sale);
        await service.CancelAsync(sale);

        Assert.Equal(5, variant.Quantity);
        Assert.Equal(0, variant.ReservedQuantity);
        Assert.Equal(0, sale.Items[0].ReservedQuantity);
        Assert.Equal(SaleStatus.Cancelled, sale.Status);
    }

    [Fact]
    public void New_sale_and_customer_have_no_prefilled_form_values()
    {
        var sale = new Sale();
        var customer = new Customer();
        Assert.Empty(sale.Number);
        Assert.Null(sale.Status);
        Assert.Null(sale.CustomerId);
        Assert.Empty(sale.Items);
        Assert.Empty(customer.Name);
        Assert.Null(customer.Phone);
    }

    private sealed class SalesMemoryCatalog(Product product) : ICatalogRepository
    {
        public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Product>>([product]);
        public Task<Product?> GetProductAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Product?>(id == product.Id ? product : null);
        public Task UpsertProductAsync(Product value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteProductAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class MemorySales : ISalesRepository
    {
        public Task<IReadOnlyList<Customer>> GetCustomersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Customer>>([]);
        public Task UpsertCustomerAsync(Customer customer, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteCustomerAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Sale>> GetSalesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Sale>>([]);
        public Task<Sale?> GetSaleAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Sale?>(null);
        public Task UpsertSaleAsync(Sale sale, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

public sealed class MarketingTests
{
    [Fact]
    public void New_marketing_forms_have_no_prefilled_values_and_outfit_sums_prices()
    {
        var collection = new ProductCollection();
        var post = new ContentPost();
        var outfit = new Outfit { Products = [new OutfitProduct { SellingPriceUzs = 120_000 }, new OutfitProduct { SellingPriceUzs = 80_000 }] };

        Assert.Empty(collection.Name);
        Assert.Null(collection.Status);
        Assert.Empty(collection.Products);
        Assert.Empty(post.Title);
        Assert.Null(post.Type);
        Assert.Null(post.ScheduledAt);
        Assert.Equal(200_000, outfit.TotalPriceUzs);
    }

    [Fact]
    public async Task Json_repository_persists_marketing_data_and_clears_deleted_links()
    {
        var root = Path.Combine(Path.GetTempPath(), "dona-crm-marketing-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var repository = new JsonMarketingRepository(new MarketingEnvironment(root));
            var collection = new ProductCollection { Name = "Лето" };
            var outfit = new Outfit { Name = "Travel" };
            var post = new ContentPost { Title = "Карусель", CollectionId = collection.Id, CollectionName = collection.Name, OutfitId = outfit.Id, OutfitName = outfit.Name };
            await repository.UpsertCollectionAsync(collection);
            await repository.UpsertOutfitAsync(outfit);
            await repository.UpsertContentPostAsync(post);

            var restored = await new JsonMarketingRepository(new MarketingEnvironment(root)).GetDataAsync();
            Assert.Single(restored.Collections);
            Assert.Single(restored.Outfits);
            Assert.Single(restored.ContentPosts);

            await repository.DeleteCollectionAsync(collection.Id);
            await repository.DeleteOutfitAsync(outfit.Id);
            var unlinked = Assert.Single((await repository.GetDataAsync()).ContentPosts);
            Assert.Null(unlinked.CollectionId);
            Assert.Null(unlinked.OutfitId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class MarketingEnvironment(string contentRootPath) : IWebHostEnvironment
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
