using Dona.Crm.Storage.Sqlite;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;

namespace Dona.Crm.Storage.Sqlite.Tests;

public sealed class SqliteSyncOperationStoreTests
{
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
