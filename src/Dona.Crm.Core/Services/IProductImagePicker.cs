using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Services;

public interface IProductImagePicker
{
    Task<ProductImage?> PickAsync(Guid productId, CancellationToken cancellationToken = default);
    Task DeleteAsync(ProductImage image, CancellationToken cancellationToken = default);
}

public interface IProductImageResolver
{
    Task<string?> ResolveAsync(ProductImage image, CancellationToken cancellationToken = default);
}
