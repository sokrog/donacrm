using Dona.Crm.Storage.Browser;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;
using Dona.Crm.Web.Storage;
using Microsoft.JSInterop;

namespace Dona.Crm.Storage.Browser.Tests;

public sealed class BrowserLocalImageStoreTests
{
    [Fact]
    public async Task Save_writes_data_url_under_image_prefix_through_donaStore_set()
    {
        var javascript = new RecordingJsRuntime();
        var store = new BrowserLocalImageStore(javascript);

        await store.SaveAsync("photo-1.jpg", [1, 2, 3], "image/jpeg");

        var call = Assert.Single(javascript.Calls, item => item.Identifier == "donaStore.set");
        Assert.Equal("image:photo-1.jpg", call.Args[0]);
        Assert.Equal($"data:image/jpeg;base64,{Convert.ToBase64String([1, 2, 3])}", call.Args[1]);
    }

    [Fact]
    public async Task Read_list_and_delete_round_trip_and_survive_a_new_instance()
    {
        var javascript = new RecordingJsRuntime();
        await new BrowserLocalImageStore(javascript).SaveAsync("a.png", [7, 8], "image/png");
        await javascript.InvokeVoidAsync("donaStore.set", "other:key", "x");
        var store = new BrowserLocalImageStore(javascript);

        var image = await store.ReadAsync("a.png");
        Assert.Equal([7, 8], image!.Content);
        Assert.Equal("image/png", image.ContentType);
        Assert.Equal(["a.png"], await store.ListKeysAsync());

        await store.DeleteAsync("a.png");

        Assert.Null(await store.ReadAsync("a.png"));
        Assert.Empty(await store.ListKeysAsync());
    }

    [Fact]
    public async Task Unsafe_keys_are_rejected_before_touching_storage()
    {
        var javascript = new RecordingJsRuntime();
        var store = new BrowserLocalImageStore(javascript);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync("../x", [1], "image/jpeg"));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadAsync("a:b"));

        Assert.Empty(javascript.Calls);
    }

    [Fact]
    public async Task Migration_moves_legacy_data_url_out_of_the_browser_snapshot()
    {
        var javascript = new RecordingJsRuntime();
        var repository = new BrowserCrmRepository(javascript);
        var product = new Product
        {
            Sku = "WEB-IMG",
            Name = "Фото",
            Images = [new ProductImage { Storage = ProductImageStorage.Local, StorageKey = "browser:p:g", Url = $"data:image/jpeg;base64,{Convert.ToBase64String([1, 2])}" }]
        };
        await ((ICatalogRepository)repository).UpsertProductAsync(product);
        var store = new BrowserLocalImageStore(javascript);

        var converted = await new LocalImageMigrationService(repository, repository, store).MigrateAsync();

        Assert.Equal(1, converted);
        var saved = Assert.Single(await repository.GetProductsAsync());
        Assert.Equal("local:browser-p-g", Assert.Single(saved.Images).Url);
        Assert.Equal([1, 2], (await store.ReadAsync("browser-p-g"))!.Content);
        Assert.DoesNotContain(javascript.Values.Where(item => !item.Key.StartsWith("image:")), item => item.Value.Contains("base64"));
    }

    private sealed class RecordingJsRuntime : IJSRuntime
    {
        public Dictionary<string, string> Values { get; } = [];
        public List<(string Identifier, object?[] Args)> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            args ??= [];
            Calls.Add((identifier, args));
            switch (identifier)
            {
                case "donaStore.get":
                    Values.TryGetValue((string)args[0]!, out var value);
                    return ValueTask.FromResult((TValue)(object?)value!);
                case "donaStore.set":
                    Values[(string)args[0]!] = (string)args[1]!;
                    return ValueTask.FromResult(default(TValue)!);
                case "donaStore.remove":
                    Values.Remove((string)args[0]!);
                    return ValueTask.FromResult(default(TValue)!);
                case "donaStore.keys":
                    var keys = Values.Keys.Where(key => key.StartsWith((string)args[0]!, StringComparison.Ordinal)).ToArray();
                    return ValueTask.FromResult((TValue)(object)keys);
                case "donaStore.requestPersistence":
                    return ValueTask.FromResult(default(TValue)!);
            }
            throw new NotSupportedException(identifier);
        }
    }
}
