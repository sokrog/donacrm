using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;

namespace Dona.Crm.Web.Storage;

public sealed class SwitchingStockMovementRepository(GoogleSheetsSettingsStore settings, JsonStockMovementRepository local, GoogleSheetsStockMovementRepository google, LoadingState loading) : IStockMovementRepository
{
    private IStockMovementRepository Current => settings.UseGoogleSheets ? google : local;
    public Task<IReadOnlyList<StockMovement>> GetAsync(CancellationToken cancellationToken = default) => loading.RunAsync("Загружаем движения склада…", () => Current.GetAsync(cancellationToken));
    public Task AddRangeAsync(IEnumerable<StockMovement> movements, CancellationToken cancellationToken = default) => loading.RunAsync("Записываем движение склада…", () => Current.AddRangeAsync(movements, cancellationToken));
}
