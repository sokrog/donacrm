using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Storage;

public sealed class SwitchingCatalogRepository(GoogleSheetsSettingsStore settings, JsonCatalogRepository local, GoogleSheetsCatalogRepository google) : ICatalogRepository
{
    private ICatalogRepository Current => settings.UseGoogleSheets ? google : local;
    public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default) => Current.GetProductsAsync(cancellationToken);
    public Task<Product?> GetProductAsync(Guid id, CancellationToken cancellationToken = default) => Current.GetProductAsync(id, cancellationToken);
    public Task UpsertProductAsync(Product product, CancellationToken cancellationToken = default) => Current.UpsertProductAsync(product, cancellationToken);
    public Task DeleteProductAsync(Guid id, CancellationToken cancellationToken = default) => Current.DeleteProductAsync(id, cancellationToken);
}
