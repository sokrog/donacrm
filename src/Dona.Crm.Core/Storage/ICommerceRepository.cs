using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Storage;

public interface ICommerceRepository
{
    Task<IReadOnlyList<Supplier>> GetSuppliersAsync(CancellationToken cancellationToken = default);
    Task UpsertSupplierAsync(Supplier supplier, CancellationToken cancellationToken = default);
    Task DeleteSupplierAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Intermediary>> GetIntermediariesAsync(CancellationToken cancellationToken = default);
    Task UpsertIntermediaryAsync(Intermediary intermediary, CancellationToken cancellationToken = default);
    Task DeleteIntermediaryAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task UpsertCategoryAsync(Category category, CancellationToken cancellationToken = default);
    Task DeleteCategoryAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Purchase>> GetPurchasesAsync(CancellationToken cancellationToken = default);
    Task<Purchase?> GetPurchaseAsync(Guid id, CancellationToken cancellationToken = default);
    Task UpsertPurchaseAsync(Purchase purchase, CancellationToken cancellationToken = default);
}

public sealed class CommerceData
{
    public List<Supplier> Suppliers { get; set; } = [];
    public List<Intermediary> Intermediaries { get; set; } = [];
    public List<Category> Categories { get; set; } = [];
    public List<Purchase> Purchases { get; set; } = [];
}
