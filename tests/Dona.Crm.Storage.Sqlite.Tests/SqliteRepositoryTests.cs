using Dona.Crm.Storage.Sqlite;
using Dona.Crm.Web.Domain;
using SQLite;

namespace Dona.Crm.Storage.Sqlite.Tests;

public sealed class SqliteRepositoryTests : IAsyncLifetime
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "dona-crm-sqlite-tests", Guid.NewGuid().ToString("N"));
    private SqliteAggregateStore? store;

    private string DatabasePath => Path.Combine(directory, "test.db3");

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(directory);
        store = new SqliteAggregateStore(new SqliteStoreOptions(DatabasePath));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (store is not null)
            await store.DisposeAsync();

        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task Catalog_seeds_once_and_preserves_nested_product_data()
    {
        var repository = new SqliteCatalogRepository(store!);
        var seeded = await repository.GetProductsAsync();
        Assert.Equal(3, seeded.Count);

        var product = new Product
        {
            Sku = "DR-TEST",
            Name = "Платье Test",
            Status = ProductStatus.InStock,
            Variants = [new ProductVariant { Color = "Чёрный", Size = "S", Quantity = 4 }],
            Images = [new ProductImage { FileName = "dress.jpg", Url = "local://dress.jpg", IsMain = true }]
        };

        await repository.UpsertProductAsync(product);
        await store!.CloseAsync();

        var reopened = new SqliteCatalogRepository(store);
        var actual = await reopened.GetProductAsync(product.Id);

        Assert.NotNull(actual);
        Assert.Equal(4, actual.Quantity);
        Assert.Equal("local://dress.jpg", actual.PrimaryImageUrl);
    }

    [Fact]
    public async Task Deleting_every_product_does_not_restore_seed_data()
    {
        var repository = new SqliteCatalogRepository(store!);
        foreach (var product in await repository.GetProductsAsync())
            await repository.DeleteProductAsync(product.Id);

        Assert.Empty(await repository.GetProductsAsync());
    }

    [Fact]
    public async Task Sales_repository_round_trips_payments_and_items()
    {
        var repository = new SqliteSalesRepository(store!);
        var sale = new Sale
        {
            Number = "SALE-TEST-001",
            Status = SaleStatus.Completed,
            Items = [new SaleItem { ProductName = "Худи Minimal", Quantity = 2, UnitPriceUzs = 279_000, SoldQuantity = 2 }],
            Payments = [new SalePayment { Type = PaymentOperationType.Payment, Status = PaymentStatus.Completed, Method = PaymentMethod.Click, AmountUzs = 558_000 }]
        };

        await repository.UpsertSaleAsync(sale);
        var actual = await repository.GetSaleAsync(sale.Id);

        Assert.NotNull(actual);
        Assert.Equal(558_000, actual.TotalUzs);
        Assert.Equal(558_000, actual.PaidUzs);
        Assert.Equal(2, actual.TotalQuantity);
    }

    [Fact]
    public async Task Database_uses_wal_journal_mode()
    {
        _ = await new SqliteCatalogRepository(store!).GetProductsAsync();
        await store!.CloseAsync();

        using var connection = new SQLiteConnection(DatabasePath);
        var mode = connection.ExecuteScalar<string>("PRAGMA journal_mode;");

        Assert.Equal("wal", mode, ignoreCase: true);
    }

    [Fact]
    public async Task Marketing_delete_clears_references_from_content_posts()
    {
        var repository = new SqliteMarketingRepository(store!);
        var collection = new ProductCollection { Name = "Summer" };
        var post = new ContentPost { Title = "Launch", CollectionId = collection.Id, CollectionName = collection.Name };

        await repository.UpsertCollectionAsync(collection);
        await repository.UpsertContentPostAsync(post);
        await repository.DeleteCollectionAsync(collection.Id);

        var actual = Assert.Single(await repository.GetContentPostsAsync());
        Assert.Null(actual.CollectionId);
        Assert.Null(actual.CollectionName);
    }

    [Fact]
    public async Task Supporting_repositories_persist_settings_stock_and_purchase_history()
    {
        var settings = new SqliteBusinessSettingsRepository(store!);
        await settings.SaveAsync(new BusinessSettings { LowStockThreshold = 7, SaleNumberPrefix = "CO" });

        var stock = new SqliteStockMovementRepository(store!);
        var movement = new StockMovement { ProductName = "Test", QuantityDelta = 3 };
        await stock.AddRangeAsync([movement]);

        var history = new SqlitePurchaseHistoryRepository(store!);
        var cost = new ProductCostHistoryEntry { Id = Guid.NewGuid(), ProductName = "Test", UnitPriceCny = 15 };
        var rate = new ExchangeRateHistoryEntry { Id = Guid.NewGuid(), RateUzs = 1_800 };
        await history.AddAsync([cost], rate);
        await history.AddAsync([cost], rate);

        Assert.Equal(7, (await settings.GetAsync()).LowStockThreshold);
        Assert.Equal(movement.Id, Assert.Single(await stock.GetAsync()).Id);
        Assert.Single((await history.GetAsync()).ProductCosts);
        Assert.Single((await history.GetAsync()).ExchangeRates);
    }
}
