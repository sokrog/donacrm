using System.IO.Compression;
using System.Net;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Core.Tests;

internal sealed class InMemoryLocalImageStore : ILocalImageStore
{
    public Dictionary<string, LocalImage> Items { get; } = new(StringComparer.Ordinal);
    public int ReadCount { get; private set; }

    public Task SaveAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken = default)
    {
        Items[LocalImageKey.Validate(key)] = new LocalImage(content, contentType);
        return Task.CompletedTask;
    }

    public Task<LocalImage?> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        ReadCount++;
        return Task.FromResult(Items.GetValueOrDefault(LocalImageKey.Validate(key)));
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        Items.Remove(LocalImageKey.Validate(key));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListKeysAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(Items.Keys.ToList());
}

public sealed class LocalImageKeyTests
{
    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("a1b2-c3_d4.webp")]
    [InlineData("browser-abc-def")]
    public void Accepts_safe_keys(string key) => Assert.True(LocalImageKey.IsValid(key));

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../evil.jpg")]
    [InlineData("a/b.jpg")]
    [InlineData("a\\b.jpg")]
    [InlineData("browser:id:guid")]
    [InlineData("фото.jpg")]
    public void Rejects_unsafe_keys(string key) => Assert.False(LocalImageKey.IsValid(key));

    [Fact]
    public void Rejects_too_long_keys() => Assert.False(LocalImageKey.IsValid(new string('a', LocalImageKey.MaxLength + 1)));

    [Fact]
    public void Sanitize_turns_legacy_browser_storage_keys_into_valid_keys()
    {
        var key = LocalImageKey.Sanitize("browser:abc:def");

        Assert.Equal("browser-abc-def", key);
        Assert.True(LocalImageKey.IsValid(LocalImageKey.Sanitize("../..")));
    }

    [Fact]
    public async Task File_store_rejects_path_traversal_and_round_trips_bytes()
    {
        var root = Path.Combine(Path.GetTempPath(), "dona-crm-core-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileLocalImageStore(root);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync("../evil.jpg", [1], "image/jpeg"));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadAsync("..\\evil.jpg"));

            await store.SaveAsync("photo.png", [1, 2, 3], "image/png");

            var image = await store.ReadAsync("photo.png");
            Assert.Equal([1, 2, 3], image!.Content);
            Assert.Equal("image/png", image.ContentType);
            Assert.Equal(["photo.png"], await store.ListKeysAsync());
            await store.DeleteAsync("photo.png");
            Assert.Null(await store.ReadAsync("photo.png"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}

public sealed class ProductImageResolverTests
{
    private static ProductImageResolver Create(InMemoryLocalImageStore store, Func<HttpRequestMessage, HttpResponseMessage>? drive = null) =>
        new(new GoogleDriveFileClient(new HttpClient(new StubHandler(drive ?? (_ => new HttpResponseMessage(HttpStatusCode.NotFound))))),
            new StaticToken(),
            store);

    [Fact]
    public async Task Resolves_local_reference_from_store_as_data_url()
    {
        var store = new InMemoryLocalImageStore();
        await store.SaveAsync("p.png", [1, 2, 3], "image/png");
        var resolver = Create(store);

        var source = await resolver.ResolveAsync(new ProductImage { Storage = ProductImageStorage.Local, StorageKey = "p.png", Url = "local:p.png" });

        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String([1, 2, 3])}", source);
    }

    [Fact]
    public async Task Missing_local_file_resolves_to_null()
    {
        var resolver = Create(new InMemoryLocalImageStore());

        Assert.Null(await resolver.ResolveAsync(new ProductImage { Storage = ProductImageStorage.Local, Url = "local:gone.jpg" }));
    }

    [Fact]
    public async Task Local_images_are_cached_and_cache_is_bounded()
    {
        var store = new InMemoryLocalImageStore();
        for (var i = 0; i < ProductImageResolver.MaxCachedImages + 5; i++)
            await store.SaveAsync($"i{i}.jpg", [1], "image/jpeg");
        var resolver = Create(store);
        ProductImage Image(int i) => new() { Storage = ProductImageStorage.Local, Url = $"local:i{i}.jpg" };

        await resolver.ResolveAsync(Image(0));
        await resolver.ResolveAsync(Image(0));
        Assert.Equal(1, store.ReadCount);

        for (var i = 1; i < ProductImageResolver.MaxCachedImages + 5; i++)
            await resolver.ResolveAsync(Image(i));
        var before = store.ReadCount;
        await resolver.ResolveAsync(Image(0));

        Assert.Equal(before + 1, store.ReadCount);
    }

    [Fact]
    public async Task Legacy_data_url_and_external_url_are_returned_as_is()
    {
        var resolver = Create(new InMemoryLocalImageStore());

        Assert.Equal("data:image/png;base64,AQID", await resolver.ResolveAsync(new ProductImage { Storage = ProductImageStorage.Local, Url = "data:image/png;base64,AQID" }));
        Assert.Equal("https://example.com/a.jpg", await resolver.ResolveAsync(new ProductImage { Storage = ProductImageStorage.External, Url = "https://example.com/a.jpg" }));
    }

    [Fact]
    public async Task Drive_image_is_downloaded_once()
    {
        var calls = 0;
        var resolver = Create(new InMemoryLocalImageStore(), _ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([9, 9]) { Headers = { ContentType = new("image/jpeg") } } };
        });
        var image = new ProductImage { Storage = ProductImageStorage.GoogleDrive, StorageKey = "file-1", Url = "drive:file-1" };

        var first = await resolver.ResolveAsync(image);
        await resolver.ResolveAsync(image);

        Assert.Equal($"data:image/jpeg;base64,{Convert.ToBase64String([9, 9])}", first);
        Assert.Equal(1, calls);
    }

    private sealed class StaticToken : IGoogleAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult("token");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}

public sealed class LocalImageMigrationTests
{
    private static string DataUrl(params byte[] bytes) => $"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}";

    [Fact]
    public async Task Converts_data_url_images_to_local_references_and_is_idempotent()
    {
        var store = new InMemoryLocalImageStore();
        var catalog = new FakeCatalog();
        var marketing = new FakeMarketing();
        var product = new Product
        {
            Sku = "M-1",
            Name = "Платье",
            ImageUrl = DataUrl(1, 2, 3),
            Variants = [new ProductVariant { Color = "Black", Size = "M", Quantity = 4, ReservedQuantity = 1 }],
            Images = [new ProductImage { Storage = ProductImageStorage.Local, StorageKey = "browser:p:g", Url = DataUrl(1, 2, 3), IsMain = true }]
        };
        catalog.Products.Add(product);
        marketing.Collections.Add(new ProductCollection { Name = "К", Images = [new ProductImage { Storage = ProductImageStorage.Local, StorageKey = "c.jpg", Url = DataUrl(7) }] });
        marketing.Outfits.Add(new Outfit { Name = "О", Images = [new ProductImage { Storage = ProductImageStorage.External, Url = "https://x/y.jpg" }] });
        var service = new LocalImageMigrationService(catalog, marketing, store);

        var converted = await service.MigrateAsync();

        Assert.Equal(2, converted);
        var image = Assert.Single(catalog.Products[0].Images);
        Assert.Equal("local:browser-p-g", image.Url);
        Assert.Equal("browser-p-g", image.StorageKey);
        Assert.Equal("local:browser-p-g", catalog.Products[0].ImageUrl);
        Assert.Equal([1, 2, 3], store.Items["browser-p-g"].Content);
        Assert.Equal("image/jpeg", store.Items["browser-p-g"].ContentType);
        Assert.Equal(4, catalog.Products[0].Variants[0].Quantity);
        Assert.Equal(1, catalog.Products[0].Variants[0].ReservedQuantity);
        Assert.Equal("local:c.jpg", marketing.Collections[0].Images[0].Url);
        Assert.Equal("https://x/y.jpg", marketing.Outfits[0].Images[0].Url);

        var upserts = catalog.UpsertCount + marketing.UpsertCount;
        Assert.Equal(0, await service.MigrateAsync());
        Assert.Equal(upserts, catalog.UpsertCount + marketing.UpsertCount);
    }

    [Fact]
    public async Task Existing_file_is_kept_and_broken_image_does_not_block_others()
    {
        var store = new InMemoryLocalImageStore();
        await store.SaveAsync("keep.jpg", [5], "image/jpeg");
        var catalog = new FakeCatalog();
        catalog.Products.Add(new Product
        {
            Sku = "M-2",
            Name = "Юбка",
            Images =
            [
                new ProductImage { Storage = ProductImageStorage.Local, StorageKey = "broken.jpg", Url = "data:image/jpeg;base64,@@@" },
                new ProductImage { Storage = ProductImageStorage.Local, StorageKey = "keep.jpg", Url = DataUrl(9, 9) }
            ]
        });

        var converted = await new LocalImageMigrationService(catalog, new FakeMarketing(), store).MigrateAsync();

        Assert.Equal(1, converted);
        Assert.Equal([5], store.Items["keep.jpg"].Content);
        Assert.StartsWith("data:", catalog.Products[0].Images[0].Url);
        Assert.Equal("local:keep.jpg", catalog.Products[0].Images[1].Url);
    }

    [Fact]
    public async Task EnsureMigratedAsync_swallows_repository_failures()
    {
        var service = new LocalImageMigrationService(new ThrowingCatalog(), new FakeMarketing(), new InMemoryLocalImageStore());

        Assert.Equal(0, await service.EnsureMigratedAsync());
    }

    private sealed class ThrowingCatalog : ICatalogRepository
    {
        public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("boom");
        public Task<Product?> GetProductAsync(Guid id, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task UpsertProductAsync(Product product, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task DeleteProductAsync(Guid id, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }

    private sealed class FakeCatalog : ICatalogRepository
    {
        public List<Product> Products { get; } = [];
        public int UpsertCount { get; private set; }
        public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Product>>(Products.ToList());
        public Task<Product?> GetProductAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Products.FirstOrDefault(item => item.Id == id));
        public Task UpsertProductAsync(Product product, CancellationToken cancellationToken = default)
        {
            UpsertCount++;
            var index = Products.FindIndex(item => item.Id == product.Id);
            if (index >= 0) Products[index] = product; else Products.Add(product);
            return Task.CompletedTask;
        }
        public Task DeleteProductAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeMarketing : IMarketingRepository
    {
        public List<ProductCollection> Collections { get; } = [];
        public List<Outfit> Outfits { get; } = [];
        public int UpsertCount { get; private set; }
        public Task<MarketingData> GetDataAsync(CancellationToken cancellationToken = default) => Task.FromResult(new MarketingData { Collections = Collections, Outfits = Outfits });
        public Task<IReadOnlyList<ProductCollection>> GetCollectionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProductCollection>>(Collections.ToList());
        public Task UpsertCollectionAsync(ProductCollection collection, CancellationToken cancellationToken = default) { UpsertCount++; return Task.CompletedTask; }
        public Task DeleteCollectionAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Outfit>> GetOutfitsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Outfit>>(Outfits.ToList());
        public Task UpsertOutfitAsync(Outfit outfit, CancellationToken cancellationToken = default) { UpsertCount++; return Task.CompletedTask; }
        public Task DeleteOutfitAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ContentPost>> GetContentPostsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ContentPost>>([]);
        public Task UpsertContentPostAsync(ContentPost post, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteContentPostAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

public sealed class BackupLocalImageTests
{
    private static ProductImage Local(string key) => new() { Storage = ProductImageStorage.Local, StorageKey = key, Url = $"local:{key}" };

    [Fact]
    public async Task Create_includes_only_referenced_images_and_inspect_counts_them()
    {
        var store = new InMemoryLocalImageStore();
        await store.SaveAsync("used.jpg", [1, 2, 3], "image/jpeg");
        await store.SaveAsync("orphan.jpg", [9], "image/jpeg");
        var snapshot = new DonaSyncSnapshot { Products = [new Product { Sku = "B-1", Name = "B", Images = [Local("used.jpg"), Local("missing.jpg")] }] };

        var images = await BackupArchiveCodec.CollectLocalImagesAsync(snapshot, store);
        var bytes = BackupArchiveCodec.Create(BackupSnapshotMapper.FromSyncSnapshot(snapshot), images);
        var inspection = BackupArchiveCodec.Inspect(bytes);

        Assert.Equal("used.jpg", Assert.Single(images).Key);
        Assert.Equal(1, inspection.LocalImageCount);
        Assert.Equal(3, inspection.LocalImageBytes);
        Assert.DoesNotContain("base64", System.Text.Encoding.UTF8.GetString(ReadEntry(bytes, "dona-crm-backup.json")));
    }

    [Fact]
    public async Task Restore_writes_archive_images_back_to_the_store()
    {
        var source = new InMemoryLocalImageStore();
        await source.SaveAsync("used.jpg", [1, 2, 3], "image/jpeg");
        var snapshot = new DonaSyncSnapshot { Products = [new Product { Sku = "B-2", Name = "B", Images = [Local("used.jpg")] }] };
        var archive = BackupArchiveCodec.Create(
            BackupSnapshotMapper.FromSyncSnapshot(snapshot),
            await BackupArchiveCodec.CollectLocalImagesAsync(snapshot, source));
        var target = new InMemoryLocalImageStore();
        var snapshotStore = new FakeSnapshotStore();

        await new BackupRestoreService(snapshotStore, target).RestoreAsync(archive);

        Assert.Equal([1, 2, 3], target.Items["used.jpg"].Content);
        Assert.Equal("local:used.jpg", snapshotStore.Replaced!.Products[0].Images[0].Url);
    }

    [Fact]
    public async Task Restore_of_legacy_archive_with_data_urls_moves_bytes_to_the_store()
    {
        var legacy = new BackupSnapshot
        {
            Products =
            [
                new Product
                {
                    Sku = "OLD-1",
                    Name = "Old",
                    Images = [new ProductImage { Storage = ProductImageStorage.Local, StorageKey = "old.jpg", Url = $"data:image/jpeg;base64,{Convert.ToBase64String([4, 5])}" }]
                }
            ]
        };
        var target = new InMemoryLocalImageStore();
        var snapshotStore = new FakeSnapshotStore();

        await new BackupRestoreService(snapshotStore, target).RestoreAsync(BackupArchiveCodec.Create(legacy));

        Assert.Equal([4, 5], target.Items["old.jpg"].Content);
        Assert.Equal("local:old.jpg", snapshotStore.Replaced!.Products[0].Images[0].Url);
    }

    [Fact]
    public void ReadImages_skips_unsafe_and_nested_entries()
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in new[] { "images/ok.jpg", "images/sub/nested.jpg", "images/bad:name.jpg" })
            {
                using var stream = archive.CreateEntry(name).Open();
                stream.Write([1]);
            }
        }

        var images = BackupArchiveCodec.ReadImages(output.ToArray());

        Assert.Equal("ok.jpg", Assert.Single(images).Key);
    }

    private static byte[] ReadEntry(byte[] zip, string name)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        using var stream = archive.GetEntry(name)!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private sealed class FakeSnapshotStore : IBackupSnapshotStore
    {
        public DonaSyncSnapshot? Replaced { get; private set; }
        public Task<DonaSyncSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(new DonaSyncSnapshot());
        public Task ReplaceSnapshotAsync(DonaSyncSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Replaced = snapshot;
            return Task.CompletedTask;
        }
    }
}
