using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Storage;

public interface IStockMovementRepository
{
    Task<IReadOnlyList<StockMovement>> GetAsync(CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<StockMovement> movements, CancellationToken cancellationToken = default);
}
