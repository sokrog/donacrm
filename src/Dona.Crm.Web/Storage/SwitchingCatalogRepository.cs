using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Storage;

public sealed class SwitchingCatalogRepository(GoogleSheetsSettingsStore settings, JsonCatalogRepository local, GoogleSheetsCatalogRepository google, Dona.Crm.Web.Services.LoadingState loading) : ICatalogRepository
{
    private ICatalogRepository Current => settings.UseGoogleSheets ? google : local;
    public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default) => loading.RunAsync("Загружаем товары…", () => Current.GetProductsAsync(cancellationToken));
    public Task<Product?> GetProductAsync(Guid id, CancellationToken cancellationToken = default) => loading.RunAsync("Загружаем товар…", () => Current.GetProductAsync(id, cancellationToken));
    public Task UpsertProductAsync(Product product, CancellationToken cancellationToken = default) => loading.RunAsync("Сохраняем товар…", () => Current.UpsertProductAsync(product, cancellationToken));
    public Task DeleteProductAsync(Guid id, CancellationToken cancellationToken = default) => loading.RunAsync("Удаляем товар…", () => Current.DeleteProductAsync(id, cancellationToken));
}
