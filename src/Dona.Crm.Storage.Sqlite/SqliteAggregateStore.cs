using System.Text.Json;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;
using SQLite;

namespace Dona.Crm.Storage.Sqlite;

public sealed class SqliteAggregateStore(SqliteStoreOptions options) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private SQLiteAsyncConnection? database;

    public string DatabasePath => options.DatabasePath;

    public async Task<IReadOnlyList<T>> ReadCollectionAsync<T>(string collection, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await GetDatabaseAsync(cancellationToken);
        var rows = await connection.Table<AggregateRecord>()
            .Where(row => row.Collection == collection)
            .OrderBy(row => row.SortOrder)
            .ToListAsync();

        cancellationToken.ThrowIfCancellationRequested();
        return rows.Select(Deserialize<T>).ToList();
    }

    public async Task ReplaceCollectionAsync<T>(
        string collection,
        IEnumerable<(Guid Id, T Value)> values,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        cancellationToken.ThrowIfCancellationRequested();

        var rows = values.Select((item, index) => new AggregateRecord
        {
            Key = CreateKey(collection, item.Id),
            Collection = collection,
            SortOrder = index,
            Payload = JsonSerializer.Serialize(item.Value, JsonOptions),
            UpdatedAtUtcTicks = DateTimeOffset.UtcNow.UtcTicks
        }).ToList();

        await writeGate.WaitAsync(cancellationToken);
        try
        {
            var connection = await GetDatabaseAsync(cancellationToken);
            await connection.RunInTransactionAsync(transaction =>
            {
                transaction.Execute("DELETE FROM aggregate_records WHERE collection = ?", collection);
                transaction.InsertAll(rows);
            });
        }
        finally
        {
            writeGate.Release();
        }
    }

    public async Task ReplaceSnapshotAsync(DonaSyncSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        var replacements = new Dictionary<string, List<AggregateRecord>>
        {
            ["catalog.products"] = Rows("catalog.products", snapshot.Products, value => value.Id),
            ["commerce.suppliers"] = Rows("commerce.suppliers", snapshot.Suppliers, value => value.Id),
            ["commerce.intermediaries"] = Rows("commerce.intermediaries", snapshot.Intermediaries, value => value.Id),
            ["commerce.categories"] = Rows("commerce.categories", snapshot.Categories, value => value.Id),
            ["commerce.purchases"] = Rows("commerce.purchases", snapshot.Purchases, value => value.Id),
            ["sales.customers"] = Rows("sales.customers", snapshot.Customers, value => value.Id),
            ["sales.sales"] = Rows("sales.sales", snapshot.Sales, value => value.Id),
            ["marketing.collections"] = Rows("marketing.collections", snapshot.Marketing.Collections, value => value.Id),
            ["marketing.outfits"] = Rows("marketing.outfits", snapshot.Marketing.Outfits, value => value.Id),
            ["marketing.posts"] = Rows("marketing.posts", snapshot.Marketing.ContentPosts, value => value.Id),
            ["settings.business"] = Rows("settings.business", [snapshot.BusinessSettings], _ => Guid.Parse("59b746f0-32f8-48a3-b1eb-ecf57969093a")),
            ["stock.movements"] = Rows("stock.movements", snapshot.StockMovements, value => value.Id),
            ["history.product-costs"] = Rows("history.product-costs", snapshot.PurchaseHistory.ProductCosts, value => value.Id),
            ["history.exchange-rates"] = Rows("history.exchange-rates", snapshot.PurchaseHistory.ExchangeRates, value => value.Id)
        };

        await writeGate.WaitAsync(cancellationToken);
        try
        {
            var connection = await GetDatabaseAsync(cancellationToken);
            await connection.RunInTransactionAsync(transaction =>
            {
                foreach (var replacement in replacements)
                {
                    transaction.Execute("DELETE FROM aggregate_records WHERE collection = ?", replacement.Key);
                    transaction.InsertAll(replacement.Value);
                }
            });
        }
        finally { writeGate.Release(); }
    }

    public async Task CloseAsync()
    {
        await writeGate.WaitAsync();
        try
        {
            await initializationGate.WaitAsync();
            try
            {
                if (database is null)
                    return;

                await database.CloseAsync();
                database = null;
            }
            finally
            {
                initializationGate.Release();
            }
        }
        finally
        {
            writeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        initializationGate.Dispose();
        writeGate.Dispose();
    }

    private async Task<SQLiteAsyncConnection> GetDatabaseAsync(CancellationToken cancellationToken)
    {
        if (database is not null)
            return database;

        await initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (database is not null)
                return database;

            Directory.CreateDirectory(Path.GetDirectoryName(options.DatabasePath)!);
            var connection = new SQLiteAsyncConnection(options.DatabasePath, options.Flags);
            try
            {
                _ = await connection.ExecuteScalarAsync<string>("PRAGMA journal_mode=WAL;");
                await connection.ExecuteAsync("PRAGMA foreign_keys=ON;");
                await connection.CreateTableAsync<SchemaVersion>();
                await connection.CreateTableAsync<AggregateRecord>();
                await connection.InsertOrReplaceAsync(new SchemaVersion { Id = 1, Version = 1 });
                database = connection;
                return connection;
            }
            catch
            {
                await connection.CloseAsync();
                throw;
            }
        }
        finally
        {
            initializationGate.Release();
        }
    }

    private static string CreateKey(string collection, Guid id) => $"{collection}:{id:N}";

    private static List<AggregateRecord> Rows<T>(string collection, IEnumerable<T> values, Func<T, Guid> id) =>
        values.Select((value, index) => new AggregateRecord
        {
            Key = CreateKey(collection, id(value)),
            Collection = collection,
            SortOrder = index,
            Payload = JsonSerializer.Serialize(value, JsonOptions),
            UpdatedAtUtcTicks = DateTimeOffset.UtcNow.UtcTicks
        }).ToList();

    private static T Deserialize<T>(AggregateRecord row) =>
        JsonSerializer.Deserialize<T>(row.Payload, JsonOptions)
        ?? throw new InvalidDataException($"SQLite aggregate '{row.Key}' has an empty payload.");

    [Table("schema_versions")]
    private sealed class SchemaVersion
    {
        [PrimaryKey]
        public int Id { get; set; }
        public int Version { get; set; }
    }

    [Table("aggregate_records")]
    private sealed class AggregateRecord
    {
        [PrimaryKey, MaxLength(160)]
        public string Key { get; set; } = string.Empty;

        [Indexed(Name = "IX_Aggregates_Collection_SortOrder", Order = 1), MaxLength(80)]
        public string Collection { get; set; } = string.Empty;

        [Indexed(Name = "IX_Aggregates_Collection_SortOrder", Order = 2)]
        public int SortOrder { get; set; }

        [NotNull]
        public string Payload { get; set; } = string.Empty;

        public long UpdatedAtUtcTicks { get; set; }
    }
}
