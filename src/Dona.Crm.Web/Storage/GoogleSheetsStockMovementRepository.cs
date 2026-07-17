using System.Globalization;
using Dona.Crm.Web.Domain;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace Dona.Crm.Web.Storage;

public sealed class GoogleSheetsStockMovementRepository(GoogleSheetsSettingsStore settings) : IStockMovementRepository, IDisposable
{
    private const string SheetName = "StockMovements";
    private static readonly string[] Headers = ["Id", "CreatedAt", "Type", "ProductId", "ProductVariantId", "ProductName", "Sku", "Color", "Size", "QuantityDelta", "ReservedDelta", "SourceType", "SourceId", "SourceNumber", "Note"];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SheetsService? _service;
    private int _version = -1;
    private bool _initialized;

    public async Task<IReadOnlyList<StockMovement>> GetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var service = await EnsureAsync(cancellationToken);
            var rows = (await service.Spreadsheets.Values.Get(settings.SpreadsheetId, $"{SheetName}!A2:O").ExecuteAsync(cancellationToken)).Values ?? [];
            return rows.Select(Parse).Where(x => x is not null).Cast<StockMovement>().ToList();
        }
        finally { _gate.Release(); }
    }

    public async Task AddRangeAsync(IEnumerable<StockMovement> movements, CancellationToken cancellationToken = default)
    {
        var rows = movements.Select(ToRow).ToList();
        if (rows.Count == 0) return;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var service = await EnsureAsync(cancellationToken);
            var request = service.Spreadsheets.Values.Append(new ValueRange { Values = rows }, settings.SpreadsheetId, $"{SheetName}!A:O");
            request.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.RAW;
            request.InsertDataOption = SpreadsheetsResource.ValuesResource.AppendRequest.InsertDataOptionEnum.INSERTROWS;
            await request.ExecuteAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task<SheetsService> EnsureAsync(CancellationToken token)
    {
        if (!settings.IsConfigured) throw new GoogleSheetsConfigurationException("Google Sheets не настроен.");
        if (_version != settings.Version) { _service?.Dispose(); _service = null; _initialized = false; _version = settings.Version; }
        _service ??= new SheetsService(new BaseClientService.Initializer { HttpClientInitializer = CredentialFactory.FromFile<ServiceAccountCredential>(settings.CredentialsFullPath).ToGoogleCredential().CreateScoped(SheetsService.Scope.Spreadsheets), ApplicationName = "Dona CRM" });
        if (_initialized) return _service;
        var spreadsheet = await _service.Spreadsheets.Get(settings.SpreadsheetId).ExecuteAsync(token);
        if (!spreadsheet.Sheets.Any(x => x.Properties.Title.Equals(SheetName, StringComparison.OrdinalIgnoreCase)))
            await _service.Spreadsheets.BatchUpdate(new BatchUpdateSpreadsheetRequest { Requests = [new Request { AddSheet = new AddSheetRequest { Properties = new SheetProperties { Title = SheetName } } }] }, settings.SpreadsheetId).ExecuteAsync(token);
        var header = _service.Spreadsheets.Values.Update(new ValueRange { Values = [Headers.Cast<object>().ToList()] }, settings.SpreadsheetId, $"{SheetName}!A1:O1");
        header.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await header.ExecuteAsync(token);
        _initialized = true;
        return _service;
    }

    private static IList<object> ToRow(StockMovement x) => [x.Id.ToString(), x.CreatedAt.ToString("O"), x.Type.ToString(), x.ProductId.ToString(), x.ProductVariantId.ToString(), x.ProductName, x.Sku, x.Color, x.Size, x.QuantityDelta, x.ReservedDelta, x.SourceType, x.SourceId?.ToString() ?? "", x.SourceNumber, x.Note ?? ""];
    private static StockMovement? Parse(IList<object> row)
    {
        var id = GuidValue(Cell(row, 0)); var productId = GuidValue(Cell(row, 3)); var variantId = GuidValue(Cell(row, 4));
        if (id is null || productId is null || variantId is null) return null;
        return new StockMovement { Id = id.Value, CreatedAt = DateValue(Cell(row, 1)), Type = Enum.TryParse<StockMovementType>(Cell(row, 2), true, out var type) ? type : StockMovementType.Adjustment, ProductId = productId.Value, ProductVariantId = variantId.Value, ProductName = Cell(row, 5), Sku = Cell(row, 6), Color = Cell(row, 7), Size = Cell(row, 8), QuantityDelta = IntValue(Cell(row, 9)), ReservedDelta = IntValue(Cell(row, 10)), SourceType = Cell(row, 11), SourceId = GuidValue(Cell(row, 12)), SourceNumber = Cell(row, 13), Note = Empty(Cell(row, 14)) };
    }
    private static string Cell(IList<object> row, int index) => index < row.Count ? Convert.ToString(row[index], CultureInfo.InvariantCulture)?.Trim() ?? "" : "";
    private static Guid? GuidValue(string value) => Guid.TryParse(value, out var result) ? result : null;
    private static int IntValue(string value) => int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;
    private static DateTimeOffset DateValue(string value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result) ? result : DateTimeOffset.UtcNow;
    private static string? Empty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    public void Dispose() { _service?.Dispose(); _gate.Dispose(); }
}
