using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;

namespace Dona.Crm.Web.Storage;

public sealed class SwitchingPurchaseHistoryRepository(GoogleSheetsSettingsStore settings, JsonPurchaseHistoryRepository local, GoogleSheetsPurchaseHistoryRepository google, LoadingState loading) : IPurchaseHistoryRepository
{
    private IPurchaseHistoryRepository Current => settings.UseGoogleSheets ? google : local;
    public Task<PurchaseHistoryData> GetAsync(CancellationToken cancellationToken = default) => loading.RunAsync("Загружаем историю закупочных цен…", () => Current.GetAsync(cancellationToken));
    public Task AddAsync(IEnumerable<ProductCostHistoryEntry> productCosts, ExchangeRateHistoryEntry? exchangeRate, CancellationToken cancellationToken = default) => loading.RunAsync("Сохраняем закупочную стоимость…", () => Current.AddAsync(productCosts, exchangeRate, cancellationToken));
}
