using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Storage;

/// <summary>
/// Один атомарный пакет изменений склада: товары, продажи и закупки сохраняются по Id (вставка или замена),
/// а движения, история себестоимости и курс — только если записи с таким Id ещё нет.
/// </summary>
public sealed record InventoryCommit(
    IReadOnlyList<Product> Products,
    IReadOnlyList<Sale> Sales,
    IReadOnlyList<Purchase> Purchases,
    IReadOnlyList<StockMovement> Movements,
    IReadOnlyList<ProductCostHistoryEntry> ProductCosts,
    ExchangeRateHistoryEntry? ExchangeRate)
{
    // Explicit corrections replace cost snapshots without adding another receipt or stock movement.
    public IReadOnlyList<ProductCostHistoryEntry> CostCorrections { get; init; } = [];
    public static InventoryCommit Empty { get; } = new([], [], [], [], [], null);

    public bool IsEmpty => Products.Count == 0 && Sales.Count == 0 && Purchases.Count == 0 && Movements.Count == 0 && ProductCosts.Count == 0 && CostCorrections.Count == 0 && ExchangeRate is null;

    public static InventoryCommit Create(
        IEnumerable<Product>? products = null,
        IEnumerable<Sale>? sales = null,
        IEnumerable<Purchase>? purchases = null,
        IEnumerable<StockMovement>? movements = null,
        IEnumerable<ProductCostHistoryEntry>? productCosts = null,
        ExchangeRateHistoryEntry? exchangeRate = null) =>
        new(products?.ToList() ?? [], sales?.ToList() ?? [], purchases?.ToList() ?? [], movements?.ToList() ?? [], productCosts?.ToList() ?? [], exchangeRate);
}

public interface IInventoryStore
{
    /// <summary>Применяет пакет целиком: либо все изменения сохранены, либо ни одного.</summary>
    Task CommitAsync(InventoryCommit commit, CancellationToken cancellationToken = default);
}
