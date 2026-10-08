using Dona.Crm.Storage.Sqlite;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;

namespace Dona.Crm.Storage.Sqlite.Tests;

public sealed partial class SqliteRepositoryTests
{
    [Fact]
    public async Task Pricing_transaction_rolls_back_history_when_product_write_fails()
    {
        var catalog = new SqliteCatalogRepository(store!);
        var history = new SqlitePricingRepository(store!);
        var pricing = new ProductPricingService(catalog, new SqliteCommerceRepository(store!), history, new SqliteInventoryStore(store!));
        var product = new Product { Sku = "FAIL-PRICE", Name = "Товар", Variants = [new()] };
        await catalog.UpsertProductAsync(product);
        using var connection = new SQLite.SQLiteConnection(DatabasePath);
        connection.Execute("CREATE TRIGGER reject_pricing BEFORE UPDATE ON aggregate_records WHEN NEW.Collection = 'catalog.products' BEGIN SELECT RAISE(ABORT, 'test write failure'); END;");
        var quote = await pricing.QuoteAsync(product.Id, PricingBasis.RemainingStock);
        var request = new PricingRequest(Guid.NewGuid(), PricingSource.Product, product.Id, [new(quote, 150, PricingInput.ManualPrice, null, 1)]);
        await Assert.ThrowsAnyAsync<Exception>(() => pricing.ApplyAsync(request));
        Assert.Null((await catalog.GetProductAsync(product.Id))!.SellingPriceUzs);
        Assert.Empty(await history.GetPriceChangesAsync());
        connection.Execute("DROP TRIGGER reject_pricing;");
        await pricing.ApplyAsync(request);
        Assert.Equal(150, (await catalog.GetProductAsync(product.Id))!.SellingPriceUzs);
        Assert.Single(await history.GetPriceChangesAsync());
    }

    [Fact]
    public async Task Price_and_history_survive_reopen_and_snapshot_replacement()
    {
        var catalog = new SqliteCatalogRepository(store!);
        var commerce = new SqliteCommerceRepository(store!);
        var history = new SqlitePricingRepository(store!);
        var pricing = new ProductPricingService(catalog, commerce, history, new SqliteInventoryStore(store!));
        var product = new Product { Sku = "PRICE", Name = "Товар", Variants = [new()] };
        StockLayerOperations.Add(product.Variants[0], 4, 400, StockLayerSource.OpeningBalance, DateTimeOffset.UtcNow, "OPENING");
        await catalog.UpsertProductAsync(product);
        var quote = await pricing.QuoteAsync(product.Id, PricingBasis.RemainingStock);
        var request = new PricingRequest(Guid.NewGuid(), PricingSource.Product, product.Id, [new(quote, 150, PricingInput.Markup, 50, 1)]);
        await pricing.ApplyAsync(request); await pricing.ApplyAsync(request);
        await store!.DisposeAsync(); store = new SqliteAggregateStore(new SqliteStoreOptions(DatabasePath));
        Assert.Equal(150, (await new SqliteCatalogRepository(store).GetProductAsync(product.Id))!.SellingPriceUzs);
        Assert.Equal(100, Assert.Single(await new SqlitePricingRepository(store).GetPriceChangesAsync()).Lines[0].Quote.UnitCost);
        var snapshot = new DonaSyncSnapshot { Products = (await new SqliteCatalogRepository(store).GetProductsAsync()).ToList(), PriceChanges = (await new SqlitePricingRepository(store).GetPriceChangesAsync()).ToList() };
        await store.ReplaceSnapshotAsync(snapshot);
        Assert.Single(await new SqlitePricingRepository(store).GetPriceChangesAsync());
    }
}
