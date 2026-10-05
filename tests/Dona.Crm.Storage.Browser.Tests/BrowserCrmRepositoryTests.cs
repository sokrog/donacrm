using Dona.Crm.Storage.Browser;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;
using Dona.Crm.Web.Storage;
using Microsoft.JSInterop;

namespace Dona.Crm.Storage.Browser.Tests;

public sealed class BrowserCrmRepositoryTests
{
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
