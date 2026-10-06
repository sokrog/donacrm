using Dona.Crm.Storage.Browser;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;
using Dona.Crm.Web.Storage;
using Microsoft.JSInterop;

namespace Dona.Crm.Storage.Browser.Tests;

public sealed class BrowserCrmRepositoryTests
{
    [Fact]
    public async Task Late_expense_updates_costs_without_receiving_twice_and_survives_reload()
    {
        var js = new KeyValueJsRuntime();
        var repository = new BrowserCrmRepository(js);
        var product = new Product { Sku = "COST", Name = "Товар" };
        await repository.UpsertProductAsync(product);
        var item = new PurchaseItem { ProductId = product.Id, ProductName = product.Name, Quantity = 2, UnitPriceCny = 100 };
        var purchase = new Purchase { Number = "COST", CurrencyCode = "UZS", Items = [item] };
        var service = new PurchaseReceivingService(repository, repository, repository);
        await service.SaveAsync(purchase);
        await service.ReceiveAsync(purchase, [new(item.Id, 2, 0)]);
        var sale = new Sale { Number = "OLD", Items = [new() { ProductId = product.Id, Quantity = 1, UnitCostUzs = 100 }] };
        await repository.UpsertSaleAsync(sale);

        purchase.Expenses.Add(new() { Name = "Доставка", Amount = 60 });
        purchase.IsCostFinalized = true;
        await service.SaveAsync(purchase);
        await service.SaveAsync(purchase);

        var reloaded = new BrowserCrmRepository(js);
        var restored = (await reloaded.GetPurchaseAsync(purchase.Id))!;
        Assert.Equal(260, restored.TotalCostUzs);
        Assert.True(restored.IsCostFinalized);
        Assert.Equal(2, restored.CostRevisions.Count);
        var actual = (await reloaded.GetProductAsync(product.Id))!;
        Assert.Equal(2, actual.Quantity);
        Assert.Equal(130, actual.CostUzs);
        var history = await ((IPurchaseHistoryRepository)reloaded).GetAsync();
        Assert.Equal(130, Assert.Single(history.ProductCosts).UnitLandedCostUzs);
        Assert.Single(await ((IStockMovementRepository)reloaded).GetAsync());
        Assert.Equal(100, (await reloaded.GetSaleAsync(sale.Id))!.Items[0].UnitCostUzs);
    }

    [Fact]
    public async Task Failed_cost_save_is_atomic_and_old_purchase_does_not_override_new_purchase_price()
    {
        var js = new KeyValueJsRuntime();
        var repository = new BrowserCrmRepository(js);
        var product = new Product { Sku = "T", Name = "Товар" };
        await repository.UpsertProductAsync(product);
        var service = new PurchaseReceivingService(repository, repository, repository);
        var old = new Purchase { Number = "OLD", CurrencyCode = "UZS", Items = [new() { ProductId = product.Id, ProductName = product.Name, Quantity = 1, UnitPriceCny = 100 }] };
        await service.SaveAsync(old);
        await service.ReceiveAsync(old, [new(old.Items[0].Id, 1, 0)]);
        old.Expenses.Add(new() { Name = "Поздняя доставка", Amount = 50 });
        js.QuotaExceeded = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(old));
        js.QuotaExceeded = false;
        Assert.Single(old.CostRevisions);
        Assert.Empty((await repository.GetPurchaseAsync(old.Id))!.Expenses);
        Assert.Equal(100, (await repository.GetProductAsync(product.Id))!.CostUzs);
        Assert.Equal(100, Assert.Single((await ((IPurchaseHistoryRepository)repository).GetAsync()).ProductCosts).UnitLandedCostUzs);

        var newer = new Purchase { Number = "NEW", CurrencyCode = "UZS", Items = [new() { ProductId = product.Id, ProductName = product.Name, Quantity = 1, UnitPriceCny = 200 }] };
        await service.SaveAsync(newer);
        await service.ReceiveAsync(newer, [new(newer.Items[0].Id, 1, 0)]);
        await service.SaveAsync(old);
        Assert.Equal(200, (await repository.GetProductAsync(product.Id))!.CostUzs);
        Assert.Equal(150, (await ((IPurchaseHistoryRepository)repository).GetAsync()).ProductCosts.Single(x => x.PurchaseId == old.Id).UnitLandedCostUzs);
    }

    [Fact]
    public async Task Snapshot_survives_a_new_repository_instance()
    {
        var javascript = new KeyValueJsRuntime();
        ICatalogRepository first = new BrowserCrmRepository(javascript);
        var product = new Product
        {
            Sku = "WEB-1",
            Name = "Browser dress",
            Variants = [new ProductVariant { Color = "Black", Size = "M", Quantity = 3 }]
        };

        await first.UpsertProductAsync(product);
        ICatalogRepository restored = new BrowserCrmRepository(javascript);

        var value = Assert.Single(await restored.GetProductsAsync());
        Assert.Equal("WEB-1", value.Sku);
        Assert.Equal(3, Assert.Single(value.Variants).Quantity);
    }

    [Fact]
    public async Task First_start_seeds_categories_without_demo_transactions()
    {
        var repository = new BrowserCrmRepository(new KeyValueJsRuntime());

        var categories = await ((ICommerceRepository)repository).GetCategoriesAsync();

        Assert.NotEmpty(categories);
        Assert.Empty(await ((ISalesRepository)repository).GetSalesAsync());
        Assert.Empty(await repository.GetProductsAsync());
    }

    [Fact]
    public async Task Quota_error_is_reported_as_readable_InvalidOperationException()
    {
        var javascript = new KeyValueJsRuntime();
        var repository = new BrowserCrmRepository(javascript);
        await repository.GetProductsAsync();
        javascript.QuotaExceeded = true;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ((ICatalogRepository)repository).UpsertProductAsync(new Product { Sku = "BIG", Name = "Большое фото" }));

        Assert.Contains("Хранилище браузера заполнено", error.Message);
    }

    [Fact]
    public async Task Sync_operations_and_checkpoint_persist_through_the_key_value_store()
    {
        var javascript = new KeyValueJsRuntime();
        var checkpoints = new BrowserGoogleSyncCheckpointStore(javascript);

        await checkpoints.WriteAsync(new GoogleSyncCheckpoint { LocalVersion = "v1", GoogleVersion = "g1" });
        var restored = await new BrowserGoogleSyncCheckpointStore(javascript).ReadAsync();

        Assert.Equal("v1", restored?.LocalVersion);
        Assert.Equal("g1", restored?.GoogleVersion);
        Assert.Contains(javascript.Keys, key => key.Contains("sync-checkpoint"));
    }

    private sealed class KeyValueJsRuntime : IJSRuntime
    {
        private readonly Dictionary<string, string> values = [];

        public bool QuotaExceeded { get; set; }
        public IReadOnlyCollection<string> Keys => values.Keys;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (identifier)
            {
                case "donaStore.get":
                    values.TryGetValue((string)args![0]!, out var value);
                    return ValueTask.FromResult((TValue)(object?)value!);
                case "donaStore.set":
                    if (QuotaExceeded) throw new JSException("Хранилище браузера заполнено. Освободите место или подключите Google Drive для фотографий.");
                    values[(string)args![0]!] = (string)args[1]!;
                    return ValueTask.FromResult(default(TValue)!);
                case "donaStore.remove":
                    values.Remove((string)args![0]!);
                    return ValueTask.FromResult(default(TValue)!);
                case "donaStore.requestPersistence":
                    return ValueTask.FromResult(default(TValue)!);
            }
            throw new NotSupportedException(identifier);
        }
    }
}
