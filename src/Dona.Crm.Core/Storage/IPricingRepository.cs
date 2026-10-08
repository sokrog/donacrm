using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Storage;

public interface IPricingRepository
{
    Task<IReadOnlyList<SellingPriceChange>> GetPriceChangesAsync(CancellationToken cancellationToken = default);
}
