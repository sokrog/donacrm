using System.Net.Http.Headers;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Web.Services;

public sealed class GoogleMobileSyncSnapshotService(
    GoogleSheetsSettingsStore settings,
    IHttpClientFactory clients,
    GoogleSheetsCatalogRepository catalog,
    GoogleSheetsCommerceRepository commerce,
    GoogleSheetsSalesRepository sales,
    GoogleSheetsMarketingRepository marketing,
    GoogleSheetsBusinessSettingsRepository businessSettings,
    GoogleSheetsStockMovementRepository movements,
    GoogleSheetsPurchaseHistoryRepository history)
{
    public async Task<GoogleSyncEnvelope> CreateAsync(string spreadsheetId, string accessToken, CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured) throw new InvalidOperationException("Google Sheets не настроен в Web-версии DONA CRM.");
        if (!string.Equals(settings.SpreadsheetId, spreadsheetId, StringComparison.Ordinal))
            throw new InvalidOperationException("Мобильное приложение и Web-сервер настроены на разные Google-таблицы.");
        await EnsureUserAccessAsync(spreadsheetId, accessToken, cancellationToken);

        var snapshot = new DonaSyncSnapshot
        {
            Products = (await catalog.GetProductsAsync(cancellationToken)).ToList(),
            Suppliers = (await commerce.GetSuppliersAsync(cancellationToken)).ToList(),
            Intermediaries = (await commerce.GetIntermediariesAsync(cancellationToken)).ToList(),
            Categories = (await commerce.GetCategoriesAsync(cancellationToken)).ToList(),
            Purchases = (await commerce.GetPurchasesAsync(cancellationToken)).ToList(),
            Customers = (await sales.GetCustomersAsync(cancellationToken)).ToList(),
            Sales = (await sales.GetSalesAsync(cancellationToken)).ToList(),
            Marketing = await marketing.GetDataAsync(cancellationToken),
            BusinessSettings = await businessSettings.GetAsync(cancellationToken),
            StockMovements = (await movements.GetAsync(cancellationToken)).ToList(),
            PurchaseHistory = await history.GetAsync(cancellationToken)
        };
        return new GoogleSyncEnvelope(DonaSyncFingerprint.Create(snapshot), DateTimeOffset.UtcNow, snapshot);
    }

    private async Task EnsureUserAccessAsync(string spreadsheetId, string accessToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) throw new UnauthorizedAccessException("Google access token не указан.");
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://sheets.googleapis.com/v4/spreadsheets/{Uri.EscapeDataString(spreadsheetId)}?fields=spreadsheetId");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await clients.CreateClient("google-mobile-oauth").SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new UnauthorizedAccessException("Google-аккаунт не имеет доступа к выбранной таблице.");
    }
}
