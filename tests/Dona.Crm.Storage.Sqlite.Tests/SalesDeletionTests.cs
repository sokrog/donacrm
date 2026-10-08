using Dona.Crm.Storage.Sqlite;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Storage.Sqlite.Tests;

public sealed partial class SqliteRepositoryTests
{
    [Fact]
    public async Task Sale_deletion_restores_fifo_and_survives_snapshot_round_trip()
    {
        var catalog = new SqliteCatalogRepository(store!);
        var sales = new SqliteSalesRepository(store!);
        var inventory = new SalesInventoryService(catalog, new SqliteInventoryStore(store!), sales: sales);
        var product = new Product { Name = "Товар", Sku = "DELETE", Variants = [new()] };
        StockLayerOperations.Add(product.Variants[0], 10, 100, StockLayerSource.OpeningBalance, DateTimeOffset.UtcNow, "OPENING");
        await catalog.UpsertProductAsync(product);
        var sale = new Sale { Number = "DELETE-SQLITE", Status = SaleStatus.Draft,
            Items = [new() { ProductId = product.Id, ProductVariantId = product.Variants[0].Id, Quantity = 3, UnitPriceUzs = 20 }] };
        await sales.UpsertSaleAsync(sale);
        await inventory.ReserveAsync(sale);
        await inventory.MarkShippedAsync(sale);
        await inventory.CompleteAsync(sale);
        await inventory.DeleteAsync(sale.Id);
        await inventory.DeleteAsync(sale.Id);
        Assert.Empty(await sales.GetSalesAsync());
        var variant = (await catalog.GetProductAsync(product.Id))!.Variants[0];
        Assert.Equal(10, variant.Quantity);
        Assert.Equal(0, variant.ReservedQuantity);
        Assert.Equal(100m, variant.Layers.Sum(x => x.RemainingValue));
        Assert.Single(await new SqliteStockMovementRepository(store!).GetAsync(), x => x.SourceType == "SaleDeletion");
        await Assert.ThrowsAsync<InvalidOperationException>(() => sales.UpsertSaleAsync(sale));
        var sync = new SqliteSyncStore(store!, catalog, new SqliteCommerceRepository(store!), sales,
            new SqliteMarketingRepository(store!), new SqliteBusinessSettingsRepository(store!),
            new SqliteStockMovementRepository(store!), new SqlitePurchaseHistoryRepository(store!));
        var snapshot = await sync.ReadAsync();
        Assert.NotNull(Assert.Single(snapshot.Sales).DeletedAt);
        await sync.ReplaceAsync(snapshot);
        Assert.Empty(await sales.GetSalesAsync());
        Assert.NotNull((await sales.GetSaleAsync(sale.Id))!.DeletedAt);
    }
}
