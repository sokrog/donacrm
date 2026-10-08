using Dona.Crm.Storage.Sqlite;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;

namespace Dona.Crm.Storage.Sqlite.Tests;

public sealed class SqliteSyncOperationStoreTests
{
    [Fact]
    public async Task Image_transfer_updates_current_references_without_replacing_business_data_or_triggering_sync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dona-crm-sync-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var database = new SqliteAggregateStore(new SqliteStoreOptions(Path.Combine(directory, "images.db3")));
        try
        {
            ProductImage Image() => new() { Storage = ProductImageStorage.Local, Url = "local:a.png", StorageKey = "a.png", Caption = "Фото" };
            var product = new Product { Name = "Исходное название", Images = [Image()], ImageUrl = "local:a.png" };
            await database.ReplaceSnapshotAsync(new() { Products = [product], Marketing = new() { Collections = [new() { Images = [Image()] }], Outfits = [new() { Images = [Image()] }] } });
            product.Name = "Изменено во время загрузки";
            await new SqliteCatalogRepository(database).UpsertProductAsync(product);
            var notifications = 0;
            database.BusinessDataChanged += (_, _) => notifications++;
            await database.ApplyUploadedImageAsync("local:a.png", new("target", "photo", "image/png", 3));
            var saved = Assert.Single(await database.ReadCollectionAsync<Product>("catalog.products"));
            Assert.Equal(product.Name, saved.Name);
            Assert.Equal("drive:target", saved.ImageUrl);
            Assert.Equal("Фото", Assert.Single(saved.Images).Caption);
            Assert.Equal("target", Assert.Single((await database.ReadCollectionAsync<ProductCollection>("marketing.collections")).Single().Images).StorageKey);
            Assert.Equal("target", Assert.Single((await database.ReadCollectionAsync<Outfit>("marketing.outfits")).Single().Images).StorageKey);
            Assert.Equal(0, notifications);
        }
        finally
        {
            await database.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Checkpoint_persists_last_successful_sync_without_a_full_snapshot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dona-crm-sync-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var database = new SqliteAggregateStore(new SqliteStoreOptions(Path.Combine(directory, "checkpoint.db3")));
        try
        {
            var store = new SqliteSyncCheckpointStore(database);
            var successfulAt = DateTimeOffset.UtcNow.AddMinutes(-2);
            await store.WriteAsync(new GoogleSyncCheckpoint
            {
                LocalVersion = "local-v1",
                GoogleVersion = "remote-v1",
                LastSuccessfulAt = successfulAt
            });

            var restored = Assert.IsType<GoogleSyncCheckpoint>(await store.ReadAsync());
            Assert.Equal("local-v1", restored.LocalVersion);
            Assert.Equal("remote-v1", restored.GoogleVersion);
            Assert.Equal(successfulAt, restored.LastSuccessfulAt);
        }
        finally
        {
            await database.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Journal_persists_the_exact_snapshot_and_status_for_retry()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dona-crm-sync-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var database = new SqliteAggregateStore(new SqliteStoreOptions(Path.Combine(directory, "sync.db3")));
        try
        {
            var journal = new SqliteSyncOperationStore(database);
            var operation = new GoogleSyncOperation
            {
                ExpectedGoogleVersion = "remote-v1",
                LocalVersion = "local-v1",
                Snapshot = new DonaSyncSnapshot { Products = [new Product { Sku = "SYNC-1", Name = "Dress" }] }
            };
            await journal.SaveAsync(operation);
            operation.Status = GoogleSyncOperationStatus.RequiresRetry;
            operation.Error = "network";
            await journal.SaveAsync(operation);

            var restored = Assert.Single(await journal.GetAsync());
            Assert.Equal(GoogleSyncOperationStatus.RequiresRetry, restored.Status);
            Assert.Equal("SYNC-1", Assert.Single(restored.Snapshot.Products).Sku);
            Assert.Equal("remote-v1", restored.ExpectedGoogleVersion);
        }
        finally
        {
            await database.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }
}
