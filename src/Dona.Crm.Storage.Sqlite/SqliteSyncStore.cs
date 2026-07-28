using Dona.Crm.Web.Services;

namespace Dona.Crm.Storage.Sqlite;

public sealed class SqliteSyncStore(
    SqliteAggregateStore store,
    SqliteCatalogRepository catalog,
    SqliteCommerceRepository commerce,
    SqliteSalesRepository sales,
    SqliteMarketingRepository marketing,
    SqliteBusinessSettingsRepository settings,
    SqliteStockMovementRepository movements,
    SqlitePurchaseHistoryRepository history) : IBackupSnapshotStore
{
    public async Task<DonaSyncSnapshot> ReadAsync(CancellationToken cancellationToken = default) => new()
    {
        Products = (await catalog.GetProductsAsync(cancellationToken)).ToList(),
        Suppliers = (await commerce.GetSuppliersAsync(cancellationToken)).ToList(),
        Intermediaries = (await commerce.GetIntermediariesAsync(cancellationToken)).ToList(),
        Categories = (await commerce.GetCategoriesAsync(cancellationToken)).ToList(),
        Purchases = (await commerce.GetPurchasesAsync(cancellationToken)).ToList(),
        Customers = (await sales.GetCustomersAsync(cancellationToken)).ToList(),
        Sales = (await sales.GetSalesAsync(cancellationToken)).ToList(),
        Marketing = await marketing.GetDataAsync(cancellationToken),
        BusinessSettings = await settings.GetAsync(cancellationToken),
        StockMovements = (await movements.GetAsync(cancellationToken)).ToList(),
        PurchaseHistory = await history.GetAsync(cancellationToken)
    };

    public Task ReplaceAsync(DonaSyncSnapshot snapshot, CancellationToken cancellationToken = default) =>
        store.ReplaceSnapshotAsync(snapshot, cancellationToken);

    Task<DonaSyncSnapshot> IBackupSnapshotStore.ReadSnapshotAsync(CancellationToken cancellationToken) => ReadAsync(cancellationToken);
    Task IBackupSnapshotStore.ReplaceSnapshotAsync(DonaSyncSnapshot snapshot, CancellationToken cancellationToken) => ReplaceAsync(snapshot, cancellationToken);
}
