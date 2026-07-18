using Dona.Crm.Web.Domain;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace Dona.Crm.Web.Storage;

public sealed class GoogleSheetsBusinessSettingsRepository(GoogleSheetsSettingsStore connection) : IBusinessSettingsRepository, IDisposable
{
    private SheetsService? _service;
    private int _version = -1;
    private bool _initialized;
    public async Task<BusinessSettings> GetAsync(CancellationToken token = default)
    {
        var service = await EnsureAsync(token);
        var rows = (await service.Spreadsheets.Values.Get(connection.SpreadsheetId, "AppSettings!A2:B").ExecuteAsync(token)).Values ?? [];
        var values = rows.Where(x => x.Count >= 2).ToDictionary(x => Convert.ToString(x[0]) ?? "", x => Convert.ToString(x[1]) ?? "", StringComparer.OrdinalIgnoreCase);
        return new BusinessSettings { SimpleInterfaceMode = Bool(values, nameof(BusinessSettings.SimpleInterfaceMode), false), AutoUpdateStockStatus = Bool(values, nameof(BusinessSettings.AutoUpdateStockStatus), true), LowStockThreshold = Int(values, nameof(BusinessSettings.LowStockThreshold), 3), CountReservedAsUnavailable = Bool(values, nameof(BusinessSettings.CountReservedAsUnavailable), true), ZeroStockStatus = Enum.TryParse<ProductStatus>(Value(values, nameof(BusinessSettings.ZeroStockStatus)), true, out var zero) ? zero : ProductStatus.OutOfStock, PurchaseDueSoonDays = Int(values, nameof(BusinessSettings.PurchaseDueSoonDays), 3), ContentPlanningHorizonDays = Int(values, nameof(BusinessSettings.ContentPlanningHorizonDays), 7), DefaultAnalyticsPeriodDays = Int(values, nameof(BusinessSettings.DefaultAnalyticsPeriodDays), 30), StaleInventoryDays = Int(values, nameof(BusinessSettings.StaleInventoryDays), 60), UseGoogleDriveImages = Bool(values, nameof(BusinessSettings.UseGoogleDriveImages), false), GoogleDriveFolderId = Empty(values, nameof(BusinessSettings.GoogleDriveFolderId)) };
    }
    public async Task SaveAsync(BusinessSettings settings, CancellationToken token = default)
    {
        var service = await EnsureAsync(token);
        await service.Spreadsheets.Values.Clear(new ClearValuesRequest(), connection.SpreadsheetId, "AppSettings!A2:B").ExecuteAsync(token);
        var rows = new[] { Pair(nameof(settings.SimpleInterfaceMode), settings.SimpleInterfaceMode), Pair(nameof(settings.AutoUpdateStockStatus), settings.AutoUpdateStockStatus), Pair(nameof(settings.LowStockThreshold), settings.LowStockThreshold), Pair(nameof(settings.CountReservedAsUnavailable), settings.CountReservedAsUnavailable), Pair(nameof(settings.ZeroStockStatus), settings.ZeroStockStatus), Pair(nameof(settings.PurchaseDueSoonDays), settings.PurchaseDueSoonDays), Pair(nameof(settings.ContentPlanningHorizonDays), settings.ContentPlanningHorizonDays), Pair(nameof(settings.DefaultAnalyticsPeriodDays), settings.DefaultAnalyticsPeriodDays), Pair(nameof(settings.StaleInventoryDays), settings.StaleInventoryDays), Pair(nameof(settings.UseGoogleDriveImages), settings.UseGoogleDriveImages), Pair(nameof(settings.GoogleDriveFolderId), settings.GoogleDriveFolderId ?? "") };
        var update = service.Spreadsheets.Values.Update(new ValueRange { Values = rows }, connection.SpreadsheetId, "AppSettings!A2:B");
        update.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await update.ExecuteAsync(token).ConfigureAwait(false);
    }
    private async Task<SheetsService> EnsureAsync(CancellationToken token)
    {
        if (!connection.IsConfigured) throw new GoogleSheetsConfigurationException("Google Sheets не настроен.");
        if (_version != connection.Version) { _service?.Dispose(); _service = null; _initialized = false; _version = connection.Version; }
        _service ??= new SheetsService(new BaseClientService.Initializer { HttpClientInitializer = CredentialFactory.FromFile<ServiceAccountCredential>(connection.CredentialsFullPath).ToGoogleCredential().CreateScoped(SheetsService.Scope.Spreadsheets), ApplicationName = "Dona CRM" });
        if (_initialized) return _service;
        var spreadsheet = await _service.Spreadsheets.Get(connection.SpreadsheetId).ExecuteAsync(token);
        if (!spreadsheet.Sheets.Any(x => x.Properties.Title.Equals("AppSettings", StringComparison.OrdinalIgnoreCase))) await _service.Spreadsheets.BatchUpdate(new BatchUpdateSpreadsheetRequest { Requests = [new Request { AddSheet = new AddSheetRequest { Properties = new SheetProperties { Title = "AppSettings" } } }] }, connection.SpreadsheetId).ExecuteAsync(token);
        var header = _service.Spreadsheets.Values.Update(new ValueRange { Values = [["Key", "Value"]] }, connection.SpreadsheetId, "AppSettings!A1:B1");
        header.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await header.ExecuteAsync(token).ConfigureAwait(false);
        _initialized = true; return _service;
    }
    private static IList<object> Pair(string key, object value) => [key, value.ToString() ?? ""];
    private static string Value(Dictionary<string, string> values, string key) => values.TryGetValue(key, out var value) ? value : "";
    private static int Int(Dictionary<string, string> values, string key, int fallback) => int.TryParse(Value(values, key), out var value) ? value : fallback;
    private static bool Bool(Dictionary<string, string> values, string key, bool fallback) => bool.TryParse(Value(values, key), out var value) ? value : fallback;
    private static string? Empty(Dictionary<string, string> values, string key) => string.IsNullOrWhiteSpace(Value(values, key)) ? null : Value(values, key);
    public void Dispose() => _service?.Dispose();
}
