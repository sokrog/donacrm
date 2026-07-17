using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Storage;

public sealed class SwitchingCommerceRepository(GoogleSheetsSettingsStore settings, JsonCommerceRepository local, GoogleSheetsCommerceRepository google, Dona.Crm.Web.Services.LoadingState loading) : ICommerceRepository
{
    private ICommerceRepository Current => settings.UseGoogleSheets ? google : local;
    public Task<IReadOnlyList<Supplier>> GetSuppliersAsync(CancellationToken cancellationToken = default) => loading.RunAsync("Загружаем поставщиков…", () => Current.GetSuppliersAsync(cancellationToken));
    public Task UpsertSupplierAsync(Supplier supplier, CancellationToken cancellationToken = default) => loading.RunAsync("Сохраняем поставщика…", () => Current.UpsertSupplierAsync(supplier, cancellationToken));
    public Task DeleteSupplierAsync(Guid id, CancellationToken cancellationToken = default) => loading.RunAsync("Удаляем поставщика…", () => Current.DeleteSupplierAsync(id, cancellationToken));
    public Task<IReadOnlyList<Intermediary>> GetIntermediariesAsync(CancellationToken cancellationToken = default) => loading.RunAsync("Загружаем посредников…", () => Current.GetIntermediariesAsync(cancellationToken));
    public Task UpsertIntermediaryAsync(Intermediary intermediary, CancellationToken cancellationToken = default) => loading.RunAsync("Сохраняем посредника…", () => Current.UpsertIntermediaryAsync(intermediary, cancellationToken));
    public Task DeleteIntermediaryAsync(Guid id, CancellationToken cancellationToken = default) => loading.RunAsync("Удаляем посредника…", () => Current.DeleteIntermediaryAsync(id, cancellationToken));
    public Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default) => loading.RunAsync("Загружаем категории…", () => Current.GetCategoriesAsync(cancellationToken));
    public Task UpsertCategoryAsync(Category category, CancellationToken cancellationToken = default) => loading.RunAsync("Сохраняем категорию…", () => Current.UpsertCategoryAsync(category, cancellationToken));
    public Task DeleteCategoryAsync(Guid id, CancellationToken cancellationToken = default) => loading.RunAsync("Удаляем категорию…", () => Current.DeleteCategoryAsync(id, cancellationToken));
    public Task<IReadOnlyList<Purchase>> GetPurchasesAsync(CancellationToken cancellationToken = default) => loading.RunAsync("Загружаем закупки…", () => Current.GetPurchasesAsync(cancellationToken));
    public Task<Purchase?> GetPurchaseAsync(Guid id, CancellationToken cancellationToken = default) => loading.RunAsync("Загружаем закупку…", () => Current.GetPurchaseAsync(id, cancellationToken));
    public Task UpsertPurchaseAsync(Purchase purchase, CancellationToken cancellationToken = default) => loading.RunAsync("Сохраняем закупку…", () => Current.UpsertPurchaseAsync(purchase, cancellationToken));
}
