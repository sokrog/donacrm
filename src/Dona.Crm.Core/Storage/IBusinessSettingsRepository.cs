using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Storage;

public interface IBusinessSettingsRepository
{
    Task<BusinessSettings> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(BusinessSettings settings, CancellationToken cancellationToken = default);
}
