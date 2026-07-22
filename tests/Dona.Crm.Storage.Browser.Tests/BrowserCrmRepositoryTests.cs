using Dona.Crm.Storage.Browser;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;
using Microsoft.JSInterop;

namespace Dona.Crm.Storage.Browser.Tests;

public sealed class BrowserCrmRepositoryTests
{
    [Fact]
    public async Task Snapshot_survives_a_new_repository_instance()
    {
        var javascript = new LocalStorageJsRuntime();
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
        var repository = new BrowserCrmRepository(new LocalStorageJsRuntime());

        var categories = await ((ICommerceRepository)repository).GetCategoriesAsync();

        Assert.NotEmpty(categories);
        Assert.Empty(await ((ISalesRepository)repository).GetSalesAsync());
        Assert.Empty(await repository.GetProductsAsync());
    }

    private sealed class LocalStorageJsRuntime : IJSRuntime
    {
        private readonly Dictionary<string, string> values = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (identifier == "localStorage.getItem")
            {
                values.TryGetValue((string)args![0]!, out var value);
                return ValueTask.FromResult((TValue)(object?)value!);
            }
            if (identifier == "localStorage.setItem")
            {
                values[(string)args![0]!] = (string)args[1]!;
                return ValueTask.FromResult(default(TValue)!);
            }
            throw new NotSupportedException(identifier);
        }
    }
}
