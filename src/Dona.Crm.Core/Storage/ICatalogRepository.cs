using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Storage;

public interface ICatalogRepository
{
    Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default);
    Task<Product?> GetProductAsync(Guid id, CancellationToken cancellationToken = default);
    Task UpsertProductAsync(Product product, CancellationToken cancellationToken = default);
    Task DeleteProductAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";
    public string Provider { get; set; } = "LocalJson";
    public string DataFile { get; set; } = "data/catalog.json";
}
