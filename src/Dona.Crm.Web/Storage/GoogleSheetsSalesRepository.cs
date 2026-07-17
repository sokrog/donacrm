using System.Globalization;
using Dona.Crm.Web.Domain;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace Dona.Crm.Web.Storage;

public sealed class GoogleSheetsSalesRepository(GoogleSheetsSettingsStore settings) : ISalesRepository, IDisposable
{
    private static readonly string[] SheetNames = ["Customers", "Sales", "SaleItems"];
    private static readonly string[][] Headers =
    [
        ["Id", "Name", "Phone", "Instagram", "Telegram", "Address", "Notes", "CreatedAt"],
        ["Id", "Number", "CustomerId", "CustomerName", "Status", "PaymentMethod", "DeliveryMethod", "DiscountUzs", "DeliveryChargeUzs", "Notes", "CreatedAt"],
        ["Id", "SaleId", "ProductId", "ProductVariantId", "ProductName", "Color", "Size", "Quantity", "UnitPriceUzs", "UnitCostUzs", "ReservedQuantity", "SoldQuantity", "ReturnedQuantity"]
    ];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SheetsService? _service;
    private int _version = -1;
    private bool _initialized;

    public async Task<IReadOnlyList<Customer>> GetCustomersAsync(CancellationToken cancellationToken = default) => (await ReadAsync(cancellationToken)).Customers;
    public async Task<IReadOnlyList<Sale>> GetSalesAsync(CancellationToken cancellationToken = default) => (await ReadAsync(cancellationToken)).Sales;
    public async Task<Sale?> GetSaleAsync(Guid id, CancellationToken cancellationToken = default) => (await ReadAsync(cancellationToken)).Sales.FirstOrDefault(x => x.Id == id);
    public Task UpsertCustomerAsync(Customer customer, CancellationToken cancellationToken = default) => MutateAsync(x => Upsert(x.Customers, customer, y => y.Id), cancellationToken);
    public Task DeleteCustomerAsync(Guid id, CancellationToken cancellationToken = default) => MutateAsync(x => x.Customers.RemoveAll(y => y.Id == id), cancellationToken);
    public Task UpsertSaleAsync(Sale sale, CancellationToken cancellationToken = default) => MutateAsync(x => Upsert(x.Sales, sale, y => y.Id), cancellationToken);

    private async Task<SalesData> ReadAsync(CancellationToken token) { await _gate.WaitAsync(token); try { return await ReadCoreAsync(token); } finally { _gate.Release(); } }
    private async Task MutateAsync(Action<SalesData> mutation, CancellationToken token) { await _gate.WaitAsync(token); try { var data = await ReadCoreAsync(token); mutation(data); await WriteCoreAsync(data, token); } finally { _gate.Release(); } }
    private async Task<SalesData> ReadCoreAsync(CancellationToken token)
    {
        var service = await EnsureAsync(token);
        var request = service.Spreadsheets.Values.BatchGet(settings.SpreadsheetId);
        request.Ranges = new[] { "Customers!A2:H", "Sales!A2:K", "SaleItems!A2:M" };
        var ranges = (await request.ExecuteAsync(token)).ValueRanges;
        var data = new SalesData
        {
            Customers = Rows(ranges, 0).Select(ParseCustomer).Where(x => x is not null).Cast<Customer>().ToList(),
            Sales = Rows(ranges, 1).Select(ParseSale).Where(x => x is not null).Cast<Sale>().ToList()
        };
        var sales = data.Sales.ToDictionary(x => x.Id);
        foreach (var row in Rows(ranges, 2))
        {
            var saleId = GuidValue(Cell(row, 1));
            if (saleId is null || !sales.TryGetValue(saleId.Value, out var sale)) continue;
            sale.Items.Add(new SaleItem { Id = GuidValue(Cell(row, 0)) ?? Guid.NewGuid(), ProductId = GuidValue(Cell(row, 2)), ProductVariantId = GuidValue(Cell(row, 3)), ProductName = Cell(row, 4), Color = Cell(row, 5), Size = Cell(row, 6), Quantity = NullableInt(Cell(row, 7)), UnitPriceUzs = NullableDecimal(Cell(row, 8)), UnitCostUzs = NullableDecimal(Cell(row, 9)), ReservedQuantity = IntValue(Cell(row, 10)), SoldQuantity = IntValue(Cell(row, 11)), ReturnedQuantity = IntValue(Cell(row, 12)) });
        }
        return data;
    }
    private async Task WriteCoreAsync(SalesData data, CancellationToken token)
    {
        var service = await EnsureAsync(token);
        await service.Spreadsheets.Values.BatchClear(new BatchClearValuesRequest { Ranges = ["Customers!A2:H", "Sales!A2:K", "SaleItems!A2:M"] }, settings.SpreadsheetId).ExecuteAsync(token);
        var values = new List<ValueRange>();
        Add(values, "Customers!A2:H", data.Customers.Select(x => (IList<object>)[x.Id.ToString(), x.Name, x.Phone ?? "", x.Instagram ?? "", x.Telegram ?? "", x.Address ?? "", x.Notes ?? "", x.CreatedAt.ToString("O")]).ToList());
        Add(values, "Sales!A2:K", data.Sales.Select(x => (IList<object>)[x.Id.ToString(), x.Number, x.CustomerId?.ToString() ?? "", x.CustomerName ?? "", x.Status?.ToString() ?? "", x.PaymentMethod?.ToString() ?? "", x.DeliveryMethod?.ToString() ?? "", Obj(x.DiscountUzs), Obj(x.DeliveryChargeUzs), x.Notes ?? "", x.CreatedAt.ToString("O")]).ToList());
        Add(values, "SaleItems!A2:M", data.Sales.SelectMany(s => s.Items.Select(x => (IList<object>)[x.Id.ToString(), s.Id.ToString(), x.ProductId?.ToString() ?? "", x.ProductVariantId?.ToString() ?? "", x.ProductName, x.Color, x.Size, Obj(x.Quantity), Obj(x.UnitPriceUzs), Obj(x.UnitCostUzs), x.ReservedQuantity, x.SoldQuantity, x.ReturnedQuantity])).ToList());
        if (values.Count > 0) await service.Spreadsheets.Values.BatchUpdate(new BatchUpdateValuesRequest { ValueInputOption = "RAW", Data = values }, settings.SpreadsheetId).ExecuteAsync(token);
    }
    private async Task<SheetsService> EnsureAsync(CancellationToken token)
    {
        if (!settings.IsConfigured) throw new GoogleSheetsConfigurationException("Google Sheets не настроен.");
        if (_version != settings.Version) { _service?.Dispose(); _service = null; _initialized = false; _version = settings.Version; }
        _service ??= new SheetsService(new BaseClientService.Initializer { HttpClientInitializer = CredentialFactory.FromFile<ServiceAccountCredential>(settings.CredentialsFullPath).ToGoogleCredential().CreateScoped(SheetsService.Scope.Spreadsheets), ApplicationName = "Dona CRM" });
        if (_initialized) return _service;
        var spreadsheet = await _service.Spreadsheets.Get(settings.SpreadsheetId).ExecuteAsync(token);
        var existing = spreadsheet.Sheets.Select(x => x.Properties.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = SheetNames.Where(x => !existing.Contains(x)).ToList();
        if (missing.Count > 0) await _service.Spreadsheets.BatchUpdate(new BatchUpdateSpreadsheetRequest { Requests = missing.Select(x => new Request { AddSheet = new AddSheetRequest { Properties = new SheetProperties { Title = x } } }).ToList() }, settings.SpreadsheetId).ExecuteAsync(token);
        var headerData = SheetNames.Select((name, i) => new ValueRange { Range = $"{name}!A1:{ColumnName(Headers[i].Length)}1", Values = [Headers[i].Cast<object>().ToList()] }).ToList();
        await _service.Spreadsheets.Values.BatchUpdate(new BatchUpdateValuesRequest { ValueInputOption = "RAW", Data = headerData }, settings.SpreadsheetId).ExecuteAsync(token);
        _initialized = true; return _service;
    }
    private static Customer? ParseCustomer(IList<object> r) { var id = GuidValue(Cell(r, 0)); return id is null ? null : new Customer { Id = id.Value, Name = Cell(r, 1), Phone = Empty(Cell(r, 2)), Instagram = Empty(Cell(r, 3)), Telegram = Empty(Cell(r, 4)), Address = Empty(Cell(r, 5)), Notes = Empty(Cell(r, 6)), CreatedAt = DateValue(Cell(r, 7)) }; }
    private static Sale? ParseSale(IList<object> r) { var id = GuidValue(Cell(r, 0)); return id is null ? null : new Sale { Id = id.Value, Number = Cell(r, 1), CustomerId = GuidValue(Cell(r, 2)), CustomerName = Empty(Cell(r, 3)), Status = Enum.TryParse<SaleStatus>(Cell(r, 4), true, out var status) ? status : null, PaymentMethod = Enum.TryParse<PaymentMethod>(Cell(r, 5), true, out var payment) ? payment : null, DeliveryMethod = Enum.TryParse<DeliveryMethod>(Cell(r, 6), true, out var delivery) ? delivery : null, DiscountUzs = NullableDecimal(Cell(r, 7)), DeliveryChargeUzs = NullableDecimal(Cell(r, 8)), Notes = Empty(Cell(r, 9)), CreatedAt = DateValue(Cell(r, 10)) }; }
    private static IList<IList<object>> Rows(IList<ValueRange> ranges, int i) => i < ranges.Count ? ranges[i].Values ?? [] : [];
    private static void Add(List<ValueRange> values, string range, IList<IList<object>> rows) { if (rows.Count > 0) values.Add(new ValueRange { Range = range, Values = rows }); }
    private static void Upsert<T>(List<T> list, T item, Func<T, Guid> id) { var i = list.FindIndex(x => id(x) == id(item)); if (i >= 0) list[i] = item; else list.Add(item); }
    private static string Cell(IList<object> row, int i) => i < row.Count ? Convert.ToString(row[i], CultureInfo.InvariantCulture)?.Trim() ?? "" : "";
    private static Guid? GuidValue(string value) => Guid.TryParse(value, out var x) ? x : null;
    private static int IntValue(string value) => int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var x) ? x : 0;
    private static int? NullableInt(string value) => string.IsNullOrWhiteSpace(value) ? null : IntValue(value);
    private static decimal? NullableDecimal(string value) => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var x) ? x : null;
    private static string? Empty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static DateTimeOffset DateValue(string value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var x) ? x : DateTimeOffset.UtcNow;
    private static object Obj<T>(T? value) where T : struct => value.HasValue ? value.Value : "";
    private static string ColumnName(int count) => ((char)('A' + count - 1)).ToString();
    public void Dispose() { _service?.Dispose(); _gate.Dispose(); }
}
