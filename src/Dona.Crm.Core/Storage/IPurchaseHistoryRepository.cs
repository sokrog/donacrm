using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Storage;

public interface IPurchaseHistoryRepository
{
    Task<PurchaseHistoryData> GetAsync(CancellationToken cancellationToken = default);
    Task AddAsync(IEnumerable<ProductCostHistoryEntry> productCosts, ExchangeRateHistoryEntry? exchangeRate, CancellationToken cancellationToken = default);
}
