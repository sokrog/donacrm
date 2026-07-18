using System.Globalization;
using Dona.Crm.Web.Domain;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace Dona.Crm.Web.Storage;

public sealed class GoogleSheetsPurchaseHistoryRepository(GoogleSheetsSettingsStore settings) : IPurchaseHistoryRepository, IDisposable
{
    private const string CostSheet = "ProductCostHistory";
    private const string RateSheet = "ExchangeRateHistory";
    private static readonly string[] CostHeaders = ["Id", "RecordedAt", "ProductId", "ProductVariantId", "ProductName", "Sku", "Color", "Size", "PurchaseId", "ReceiptId", "PurchaseNumber", "SupplierId", "SupplierName", "Quantity", "UnitPriceCny", "CnyRateUzs", "UnitLandedCostUzs"];
    private static readonly string[] RateHeaders = ["Id", "RecordedAt", "Currency", "RateUzs", "PurchaseId", "ReceiptId", "PurchaseNumber", "SupplierName"];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SheetsService? _service;
    private int _version = -1;
    private bool _initialized;

    public async Task<PurchaseHistoryData> GetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await ReadCoreAsync(await EnsureAsync(cancellationToken), cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task AddAsync(IEnumerable<ProductCostHistoryEntry> productCosts, ExchangeRateHistoryEntry? exchangeRate, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var service = await EnsureAsync(cancellationToken);
            var existing = await ReadCoreAsync(service, cancellationToken);
            var costIds = existing.ProductCosts.Select(x => x.Id).ToHashSet();
            var costRows = productCosts.Where(x => costIds.Add(x.Id)).Select(ToCostRow).ToList();
            if (costRows.Count > 0) await AppendAsync(service, CostSheet, "A:Q", costRows, cancellationToken);
            if (exchangeRate is not null && existing.ExchangeRates.All(x => x.Id != exchangeRate.Id))
                await AppendAsync(service, RateSheet, "A:H", [ToRateRow(exchangeRate)], cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task<PurchaseHistoryData> ReadCoreAsync(SheetsService service, CancellationToken token)
    {
        var request = service.Spreadsheets.Values.BatchGet(settings.SpreadsheetId);
        request.Ranges = new[] { $"{CostSheet}!A2:Q", $"{RateSheet}!A2:H" };
        var response = await request.ExecuteAsync(token);
        return new PurchaseHistoryData
        {
            ProductCosts = (response.ValueRanges.ElementAtOrDefault(0)?.Values ?? []).Select(ParseCost).Where(x => x is not null).Cast<ProductCostHistoryEntry>().ToList(),
            ExchangeRates = (response.ValueRanges.ElementAtOrDefault(1)?.Values ?? []).Select(ParseRate).Where(x => x is not null).Cast<ExchangeRateHistoryEntry>().ToList()
        };
    }

    private async Task<SheetsService> EnsureAsync(CancellationToken token)
    {
        if (!settings.IsConfigured) throw new GoogleSheetsConfigurationException("Google Sheets не настроен.");
        if (_version != settings.Version) { _service?.Dispose(); _service = null; _initialized = false; _version = settings.Version; }
        _service ??= new SheetsService(new BaseClientService.Initializer { HttpClientInitializer = CredentialFactory.FromFile<ServiceAccountCredential>(settings.CredentialsFullPath).ToGoogleCredential().CreateScoped(SheetsService.Scope.Spreadsheets), ApplicationName = "Dona CRM" });
        if (_initialized) return _service;
        var spreadsheet = await _service.Spreadsheets.Get(settings.SpreadsheetId).ExecuteAsync(token);
        var names = spreadsheet.Sheets.Select(x => x.Properties.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requests = new List<Request>();
        if (!names.Contains(CostSheet)) requests.Add(new Request { AddSheet = new AddSheetRequest { Properties = new SheetProperties { Title = CostSheet } } });
        if (!names.Contains(RateSheet)) requests.Add(new Request { AddSheet = new AddSheetRequest { Properties = new SheetProperties { Title = RateSheet } } });
        if (requests.Count > 0) await _service.Spreadsheets.BatchUpdate(new BatchUpdateSpreadsheetRequest { Requests = requests }, settings.SpreadsheetId).ExecuteAsync(token);
        await UpdateHeaderAsync(_service, CostSheet, "A1:Q1", CostHeaders, token);
        await UpdateHeaderAsync(_service, RateSheet, "A1:H1", RateHeaders, token);
        _initialized = true;
        return _service;
    }

    private async Task UpdateHeaderAsync(SheetsService service, string sheet, string range, string[] headers, CancellationToken token)
    {
        var update = service.Spreadsheets.Values.Update(new ValueRange { Values = [headers.Cast<object>().ToList()] }, settings.SpreadsheetId, $"{sheet}!{range}");
        update.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await update.ExecuteAsync(token);
    }

    private async Task AppendAsync(SheetsService service, string sheet, string range, IList<IList<object>> rows, CancellationToken token)
    {
        var append = service.Spreadsheets.Values.Append(new ValueRange { Values = rows }, settings.SpreadsheetId, $"{sheet}!{range}");
        append.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.RAW;
        append.InsertDataOption = SpreadsheetsResource.ValuesResource.AppendRequest.InsertDataOptionEnum.INSERTROWS;
        await append.ExecuteAsync(token);
    }

    private static IList<object> ToCostRow(ProductCostHistoryEntry x) => [x.Id.ToString(), x.RecordedAt.ToString("O"), x.ProductId.ToString(), x.ProductVariantId?.ToString() ?? "", x.ProductName, x.Sku, x.Color, x.Size, x.PurchaseId.ToString(), x.ReceiptId.ToString(), x.PurchaseNumber, x.SupplierId?.ToString() ?? "", x.SupplierName, x.Quantity, x.UnitPriceCny, x.CnyRateUzs, x.UnitLandedCostUzs];
    private static IList<object> ToRateRow(ExchangeRateHistoryEntry x) => [x.Id.ToString(), x.RecordedAt.ToString("O"), x.Currency, x.RateUzs, x.PurchaseId.ToString(), x.ReceiptId.ToString(), x.PurchaseNumber, x.SupplierName];
    private static ProductCostHistoryEntry? ParseCost(IList<object> row)
    {
        var id = GuidValue(Cell(row, 0)); var productId = GuidValue(Cell(row, 2)); var purchaseId = GuidValue(Cell(row, 8)); var receiptId = GuidValue(Cell(row, 9));
        if (id is null || productId is null || purchaseId is null || receiptId is null) return null;
        return new ProductCostHistoryEntry { Id = id.Value, RecordedAt = DateValue(Cell(row, 1)), ProductId = productId.Value, ProductVariantId = GuidValue(Cell(row, 3)), ProductName = Cell(row, 4), Sku = Cell(row, 5), Color = Cell(row, 6), Size = Cell(row, 7), PurchaseId = purchaseId.Value, ReceiptId = receiptId.Value, PurchaseNumber = Cell(row, 10), SupplierId = GuidValue(Cell(row, 11)), SupplierName = Cell(row, 12), Quantity = IntValue(Cell(row, 13)), UnitPriceCny = DecimalValue(Cell(row, 14)), CnyRateUzs = DecimalValue(Cell(row, 15)), UnitLandedCostUzs = DecimalValue(Cell(row, 16)) };
    }
    private static ExchangeRateHistoryEntry? ParseRate(IList<object> row)
    {
        var id = GuidValue(Cell(row, 0)); var purchaseId = GuidValue(Cell(row, 4)); var receiptId = GuidValue(Cell(row, 5));
        if (id is null || purchaseId is null || receiptId is null) return null;
        return new ExchangeRateHistoryEntry { Id = id.Value, RecordedAt = DateValue(Cell(row, 1)), Currency = Cell(row, 2), RateUzs = DecimalValue(Cell(row, 3)), PurchaseId = purchaseId.Value, ReceiptId = receiptId.Value, PurchaseNumber = Cell(row, 6), SupplierName = Cell(row, 7) };
    }
    private static string Cell(IList<object> row, int index) => index < row.Count ? Convert.ToString(row[index], CultureInfo.InvariantCulture)?.Trim() ?? "" : "";
    private static Guid? GuidValue(string value) => Guid.TryParse(value, out var result) ? result : null;
    private static int IntValue(string value) => int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;
    private static decimal DecimalValue(string value) => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;
    private static DateTimeOffset DateValue(string value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result) ? result : DateTimeOffset.UtcNow;
    public void Dispose() { _service?.Dispose(); _gate.Dispose(); }
}
