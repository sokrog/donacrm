using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;

namespace Dona.Crm.Core.Tests;

public sealed class PortableBackupTests
{
    [Fact]
    public async Task Portable_archive_preserves_price_operation_and_unknown_cost_snapshot()
    {
        var product = new Product { Sku = "PRICE", Name = "Товар", SellingPriceUzs = 150, Variants = [new()] };
        var quote = ProductPricingService.BuildQuote(product, PricingBasis.RemainingStock) with { CurrentPrice = null };
        var operation = new SellingPriceChange(Guid.NewGuid(), DateTimeOffset.UtcNow, PricingSource.Product, product.Id, "request",
            [new(quote, 150, PricingInput.ManualPrice, null, 1, null)]);
        var source = new SnapshotStore(new() { Products = [product], PriceChanges = [operation] });
        var archive = await Create(source, new(), _ => throw new Exception("No network expected")).ExportAsync();
        var target = new SnapshotStore(new());
        await new BackupRestoreService(target, new InMemoryLocalImageStore()).RestoreAsync(archive.Content);
        Assert.Equal(DonaSyncFingerprint.Create(source.Value), DonaSyncFingerprint.Create(target.Value));
        Assert.Null(Assert.Single(target.Value.PriceChanges).Lines[0].Quote.UnitCost);
        Assert.Equal(150, target.Value.Products[0].SellingPriceUzs);
    }

    [Fact]
    public async Task Full_archive_round_trips_shared_drive_local_external_and_legacy_images_without_source_account()
    {
        var cloud = new ProductImage { Storage = ProductImageStorage.GoogleDrive, StorageKey = "cloud1", Url = "drive:cloud1", Caption = "Подпись", IsMain = true };
        var state = new SnapshotStore(new()
        {
            Products = [new() { Name = "Товар", Images = [cloud, Local("old")], ImageUrl = cloud.Url },
                new() { Name = "Старый товар", ImageUrl = "data:image/png;base64,BAUG" }],
            Marketing = new() { Collections = [new() { Images = [new() { Storage = ProductImageStorage.GoogleDrive, StorageKey = "cloud1" }] }],
                Outfits = [new() { Images = [new() { Storage = ProductImageStorage.External, Url = "https://images.example/photo" }] }] }
        });
        var before = DonaSyncFingerprint.Create(state.Value);
        var local = new InMemoryLocalImageStore();
        await local.SaveAsync("old", [4, 5, 6], "image/png");
        var driveReads = 0;
        var service = Create(state, local, request =>
        {
            if (request.RequestUri!.Host == "www.googleapis.com")
            {
                driveReads++;
                Assert.Equal("secret", request.Headers.Authorization!.Parameter);
            }
            else Assert.Null(request.Headers.Authorization);
            return ImageResponse([1, 2, 3]);
        });
        var archive = await service.ExportAsync();
        Assert.Equal(before, DonaSyncFingerprint.Create(state.Value));
        Assert.Equal(1, driveReads);
        var inspected = BackupArchiveCodec.Inspect(archive.Content);
        Assert.True(inspected.Snapshot.IncludesAllImages);
        Assert.Equal(2, inspected.LocalImageCount);
        var restored = new SnapshotStore(new());
        var restoredImages = new InMemoryLocalImageStore();
        await new BackupRestoreService(restored, restoredImages).RestoreAsync(archive.Content);
        Assert.All(LocalImageKey.EnumerateImages(restored.Value), image =>
        {
            Assert.Equal(ProductImageStorage.Local, image.Storage);
            Assert.True(restoredImages.Items.ContainsKey(image.StorageKey));
        });
        Assert.Equal(cloud.Id, restored.Value.Products[0].Images[0].Id);
        Assert.Equal("Подпись", restored.Value.Products[0].Images[0].Caption);
        Assert.Equal(restored.Value.Products[0].PrimaryImage!.Url, restored.Value.Products[0].ImageUrl);
    }

    [Fact]
    public async Task Missing_local_and_inaccessible_drive_images_report_all_failures_and_do_not_change_data()
    {
        var store = new SnapshotStore(new() { Products = [new() { Images = [Local("missing"), new() { Storage = ProductImageStorage.GoogleDrive, StorageKey = "gone", FileName = "cloud.jpg" }] }] });
        var before = DonaSyncFingerprint.Create(store.Value);
        var service = Create(store, new(), _ => new(HttpStatusCode.NotFound));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExportAsync());
        Assert.Contains("Фото 1", error.Message);
        Assert.Contains("Фото 2", error.Message);
        Assert.Equal(before, DonaSyncFingerprint.Create(store.Value));
    }

    [Fact]
    public async Task Portable_archive_with_missing_image_is_rejected_before_restore()
    {
        var store = new SnapshotStore(new() { Products = [new() { Images = [Local("one.png")] }] });
        var local = new InMemoryLocalImageStore();
        await local.SaveAsync("one.png", [1], "image/png");
        var full = await Create(store, local, _ => throw new Exception()).ExportAsync();
        using var stream = new MemoryStream();
        stream.Write(full.Content);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
            archive.Entries.First(x => x.FullName.StartsWith("images/")).Delete();
        Assert.Throws<InvalidOperationException>(() => BackupArchiveCodec.Inspect(stream.ToArray()));
    }

    [Fact]
    public async Task Upload_failure_can_resume_without_reuploading_completed_images_and_keeps_local_bytes()
    {
        var store = new SnapshotStore(new() { Products = [new() { Images = [Local("a.png"), Local("b.png")], ImageUrl = "local:a.png" }],
            Marketing = new() { Outfits = [new() { Images = [Local("a.png")] }] } });
        var local = new InMemoryLocalImageStore();
        await local.SaveAsync("a.png", [1], "image/png");
        await local.SaveAsync("b.png", [2], "image/png");
        var calls = 0;
        var fail = true;
        var service = Create(store, local, _ =>
        {
            calls++;
            if (calls == 2 && fail) return new(HttpStatusCode.ServiceUnavailable);
            return new(HttpStatusCode.OK) { Content = new StringContent($"{{\"id\":\"new{calls}\",\"name\":\"photo\",\"mimeType\":\"image/png\",\"size\":\"1\"}}", Encoding.UTF8, "application/json") };
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UploadLocalImagesAsync(store));
        Assert.Equal(ProductImageStorage.GoogleDrive, store.Value.Products[0].Images[0].Storage);
        Assert.Equal(ProductImageStorage.Local, store.Value.Products[0].Images[1].Storage);
        Assert.Equal("drive:new1", store.Value.Products[0].ImageUrl);
        Assert.Equal("new1", store.Value.Marketing.Outfits[0].Images[0].StorageKey);
        fail = false;
        Assert.Equal(1, await service.UploadLocalImagesAsync(store));
        Assert.Equal(3, calls);
        Assert.Equal(2, local.Items.Count);
        Assert.Equal(0, await service.UploadLocalImagesAsync(store));
    }

    [Fact]
    public async Task Cancellation_does_not_produce_an_archive()
    {
        var store = new SnapshotStore(new() { Products = [new() { Images = [Local("a")] }] });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(store, new(), _ => throw new Exception()).ExportAsync(cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task External_html_is_rejected_as_an_image()
    {
        var store = new SnapshotStore(new() { Products = [new() { Images = [new() { Storage = ProductImageStorage.External, Url = "https://images.example/photo" }] }] });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Create(store, new(), _ => new(HttpStatusCode.OK) { Content = new StringContent("<html/>") }).ExportAsync());
        Assert.Contains("Неподдерживаемый формат", error.Message);
    }

    private static ProductImage Local(string key) => new() { Storage = ProductImageStorage.Local, StorageKey = key, Url = $"local:{key}", FileName = key };
    private static PortableBackupService Create(SnapshotStore store, InMemoryLocalImageStore images, Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var http = new HttpClient(new Handler(handler));
        return new(store, images, new(http), new Token(), http);
    }
    private static HttpResponseMessage ImageResponse(byte[] bytes)
    {
        var result = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        result.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return result;
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(handler(request));
    }
    private sealed class Token : IGoogleAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult("secret");
    }
    private sealed class SnapshotStore(DonaSyncSnapshot value) : IBackupSnapshotStore, IImageTransferStore
    {
        public DonaSyncSnapshot Value { get; private set; } = value;
        public Task<DonaSyncSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);
        public Task ReplaceSnapshotAsync(DonaSyncSnapshot snapshot, CancellationToken cancellationToken = default) { Value = snapshot; return Task.CompletedTask; }
        public Task ApplyUploadedImageAsync(string localUrl, GoogleDriveFile file, CancellationToken cancellationToken = default)
        { ImageTransferReferences.Apply(Value, localUrl, file); return Task.CompletedTask; }
    }
}
