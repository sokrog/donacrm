namespace Dona.Crm.Web.Storage;

public sealed class CatalogMigrationService(JsonCatalogRepository local, GoogleSheetsCatalogRepository googleSheets)
{
    public async Task<int> ImportLocalToGoogleSheetsAsync(CancellationToken cancellationToken = default)
    {
        var products = await local.GetProductsAsync(cancellationToken);
        await googleSheets.ReplaceAllAsync(products, cancellationToken);
        return products.Count;
    }
}
