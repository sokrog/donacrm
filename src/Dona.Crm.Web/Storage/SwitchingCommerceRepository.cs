using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Storage;

public sealed class SwitchingCommerceRepository(GoogleSheetsSettingsStore settings, JsonCommerceRepository local, GoogleSheetsCommerceRepository google) : ICommerceRepository
{
    private ICommerceRepository Current => settings.UseGoogleSheets ? google : local;
    public Task<IReadOnlyList<Supplier>> GetSuppliersAsync(CancellationToken cancellationToken = default) => Current.GetSuppliersAsync(cancellationToken);
    public Task UpsertSupplierAsync(Supplier supplier, CancellationToken cancellationToken = default) => Current.UpsertSupplierAsync(supplier, cancellationToken);
    public Task DeleteSupplierAsync(Guid id, CancellationToken cancellationToken = default) => Current.DeleteSupplierAsync(id, cancellationToken);
    public Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default) => Current.GetCategoriesAsync(cancellationToken);
    public Task UpsertCategoryAsync(Category category, CancellationToken cancellationToken = default) => Current.UpsertCategoryAsync(category, cancellationToken);
    public Task<IReadOnlyList<Purchase>> GetPurchasesAsync(CancellationToken cancellationToken = default) => Current.GetPurchasesAsync(cancellationToken);
    public Task<Purchase?> GetPurchaseAsync(Guid id, CancellationToken cancellationToken = default) => Current.GetPurchaseAsync(id, cancellationToken);
    public Task UpsertPurchaseAsync(Purchase purchase, CancellationToken cancellationToken = default) => Current.UpsertPurchaseAsync(purchase, cancellationToken);
}
