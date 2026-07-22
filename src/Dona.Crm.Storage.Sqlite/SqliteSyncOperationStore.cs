using Dona.Crm.Web.Services;

namespace Dona.Crm.Storage.Sqlite;

public sealed class SqliteSyncOperationStore(SqliteAggregateStore store)
{
    private const string Collection = "sync.operations";
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<IReadOnlyList<GoogleSyncOperation>> GetAsync(CancellationToken cancellationToken = default) =>
        (await store.ReadCollectionAsync<GoogleSyncOperation>(Collection, cancellationToken))
        .OrderByDescending(value => value.CreatedAt)
        .ToList();

    public async Task SaveAsync(GoogleSyncOperation operation, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var operations = (await store.ReadCollectionAsync<GoogleSyncOperation>(Collection, cancellationToken)).ToList();
            var index = operations.FindIndex(value => value.Id == operation.Id);
            if (index >= 0) operations[index] = operation;
            else operations.Add(operation);
            await store.ReplaceCollectionAsync(Collection, operations.OrderBy(value => value.CreatedAt).Select(value => (value.Id, value)), cancellationToken);
        }
        finally { gate.Release(); }
    }
}
