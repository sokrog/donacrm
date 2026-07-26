using Dona.Crm.Web.Services;

namespace Dona.Crm.Storage.Sqlite;

public sealed class SqliteSyncCheckpointStore(SqliteAggregateStore store) : IGoogleSyncCheckpointStore
{
    private const string Collection = "sync.checkpoint";
    private static readonly Guid CheckpointId = new("f674dc77-6a1a-4dc8-8729-8a68a72d0a55");
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<GoogleSyncCheckpoint?> ReadAsync(CancellationToken cancellationToken = default) =>
        (await store.ReadCollectionAsync<GoogleSyncCheckpoint>(Collection, cancellationToken)).FirstOrDefault();

    public async Task WriteAsync(GoogleSyncCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await store.ReplaceCollectionAsync(Collection, [(CheckpointId, checkpoint)], cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }
}
