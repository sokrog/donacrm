using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Storage;

public sealed class SwitchingCatalogRepository(GoogleSheetsSettingsStore settings, JsonCatalogRepository local, GoogleSheetsCatalogRepository google, Dona.Crm.Web.Services.LoadingState loading, IBusinessSettingsRepository businessSettings, Dona.Crm.Web.Services.ProductStatusService statusService) : ICatalogRepository
{
    private ICatalogRepository Current => settings.UseGoogleSheets ? google : local;
    public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default) => loading.RunAsync("Загружаем товары…", () => Current.GetProductsAsync(cancellationToken));
    public Task<Product?> GetProductAsync(Guid id, CancellationToken cancellationToken = default) => loading.RunAsync("Загружаем товар…", () => Current.GetProductAsync(id, cancellationToken));
    public async Task UpsertProductAsync(Product product, CancellationToken cancellationToken = default) { var rules = await businessSettings.GetAsync(cancellationToken); product.Status = statusService.Calculate(product, rules); await loading.RunAsync("Сохраняем товар…", () => Current.UpsertProductAsync(product, cancellationToken)); }
    public Task DeleteProductAsync(Guid id, CancellationToken cancellationToken = default) => loading.RunAsync("Удаляем товар…", () => Current.DeleteProductAsync(id, cancellationToken));
}
