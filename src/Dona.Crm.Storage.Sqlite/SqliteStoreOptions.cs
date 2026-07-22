using SQLite;

namespace Dona.Crm.Storage.Sqlite;

public sealed class SqliteStoreOptions
{
    public const string DatabaseFilename = "dona-crm.db3";

    public SqliteStoreOptions(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = Path.GetFullPath(databasePath);
    }

    public string DatabasePath { get; }

    public SQLiteOpenFlags Flags { get; init; } =
        SQLiteOpenFlags.ReadWrite |
        SQLiteOpenFlags.Create |
        SQLiteOpenFlags.SharedCache;
}
