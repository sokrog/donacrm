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

    public async Task<GoogleSyncOperation> EnqueueAsync(
        GoogleSyncOperation operation,
        bool replaceExpectedGoogleVersion,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var items = (await store.ReadCollectionAsync<GoogleSyncOperation>(Collection, cancellationToken)).ToList();
            var pending = items
                .Where(value => value.Status != GoogleSyncOperationStatus.Applied)
                .OrderBy(value => value.CreatedAt)
                .FirstOrDefault();

            if (pending is null)
            {
                items.Add(operation);
                pending = operation;
            }
            else
            {
                if (replaceExpectedGoogleVersion || string.IsNullOrWhiteSpace(pending.ExpectedGoogleVersion))
                    pending.ExpectedGoogleVersion = operation.ExpectedGoogleVersion;
                pending.LocalVersion = operation.LocalVersion;
                pending.Snapshot = operation.Snapshot;
                pending.Status = GoogleSyncOperationStatus.Prepared;
                pending.AppliedAt = null;
                pending.Error = null;
                items.RemoveAll(value => value.Status != GoogleSyncOperationStatus.Applied && value.Id != pending.Id);
            }

            await store.ReplaceCollectionAsync(
                Collection,
                items.OrderBy(value => value.CreatedAt).Select(value => (value.Id, value)),
                cancellationToken);
            return pending;
        }
        finally { gate.Release(); }
    }

    public async Task ClearPendingAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var applied = (await store.ReadCollectionAsync<GoogleSyncOperation>(Collection, cancellationToken))
                .Where(value => value.Status == GoogleSyncOperationStatus.Applied)
                .OrderBy(value => value.CreatedAt)
                .Select(value => (value.Id, value));
            await store.ReplaceCollectionAsync(Collection, applied, cancellationToken);
        }
        finally { gate.Release(); }
    }
}
