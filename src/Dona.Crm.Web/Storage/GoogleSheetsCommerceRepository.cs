using System.Globalization;
using Dona.Crm.Web.Domain;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace Dona.Crm.Web.Storage;

public sealed class GoogleSheetsCommerceRepository(GoogleSheetsSettingsStore settings) : ICommerceRepository, IDisposable
{
    private static readonly string[] SheetNames = ["Suppliers", "Categories", "Purchases", "PurchaseItems", "PurchaseReceipts", "PurchaseReceiptItems", "Intermediaries"];
    private static readonly string[][] Headers =
    [
        ["Id", "Name", "Platform", "StoreUrl", "Rating", "Moq", "Contact", "WeChat", "Intermediary", "Notes", "CreatedAt"],
        ["Id", "Name", "SortOrder", "IsActive"],
        ["Id", "Number", "SupplierId", "SupplierName", "Status", "OrderedAt", "CnyRateUzs", "AgentCommissionPercent", "InternationalShippingUzs", "OtherCostsUzs", "Notes", "TrackingCode", "EstimatedDeliveryDate", "IntermediaryId", "IntermediaryName", "CurrencyCode"],
        ["Id", "PurchaseId", "ProductId", "ProductName", "Quantity", "UnitPriceCny", "UnitWeightKg", "ProductVariantId", "Color", "Size", "ReceivedQuantity", "DefectQuantity", "StockedQuantity"],
        ["Id", "PurchaseId", "ReceivedAt", "Note"],
        ["Id", "ReceiptId", "PurchaseItemId", "ProductId", "ProductVariantId", "ProductName", "Color", "Size", "ReceivedQuantity", "DefectQuantity", "StockedQuantity"],
        ["Id", "Name", "Company", "ChinaWarehouseAddress", "ContactName", "Telegram", "WeChat", "Phone", "RatePerKgUsd", "CommissionPercent", "MinimumWeightKg", "EstimatedDays", "OfficialImport", "Notes", "CreatedAt", "Rating"]
    ];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SheetsService? _service;
    private int _version = -1;
    private bool _initialized;

    public async Task<IReadOnlyList<Supplier>> GetSuppliersAsync(CancellationToken cancellationToken = default) => (await ReadAsync(cancellationToken)).Suppliers;
    public async Task<IReadOnlyList<Intermediary>> GetIntermediariesAsync(CancellationToken cancellationToken = default) => (await ReadAsync(cancellationToken)).Intermediaries;
    public async Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default) => (await ReadAsync(cancellationToken)).Categories;
    public async Task<IReadOnlyList<Purchase>> GetPurchasesAsync(CancellationToken cancellationToken = default) => (await ReadAsync(cancellationToken)).Purchases;
    public async Task<Purchase?> GetPurchaseAsync(Guid id, CancellationToken cancellationToken = default) => (await ReadAsync(cancellationToken)).Purchases.FirstOrDefault(x => x.Id == id);
    public Task UpsertSupplierAsync(Supplier supplier, CancellationToken cancellationToken = default) => MutateAsync(data => Upsert(data.Suppliers, supplier, x => x.Id), cancellationToken);
    public Task DeleteSupplierAsync(Guid id, CancellationToken cancellationToken = default) => MutateAsync(data => data.Suppliers.RemoveAll(x => x.Id == id), cancellationToken);
    public Task UpsertIntermediaryAsync(Intermediary intermediary, CancellationToken cancellationToken = default) => MutateAsync(data => Upsert(data.Intermediaries, intermediary, x => x.Id), cancellationToken);
    public Task DeleteIntermediaryAsync(Guid id, CancellationToken cancellationToken = default) => MutateAsync(data => data.Intermediaries.RemoveAll(x => x.Id == id), cancellationToken);
    public Task UpsertCategoryAsync(Category category, CancellationToken cancellationToken = default) => MutateAsync(data => Upsert(data.Categories, category, x => x.Id), cancellationToken);
    public Task DeleteCategoryAsync(Guid id, CancellationToken cancellationToken = default) => MutateAsync(data => data.Categories.RemoveAll(x => x.Id == id), cancellationToken);
    public Task UpsertPurchaseAsync(Purchase purchase, CancellationToken cancellationToken = default) => MutateAsync(data => Upsert(data.Purchases, purchase, x => x.Id), cancellationToken);

    private async Task<CommerceData> ReadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await ReadCoreAsync(cancellationToken); }
        finally { _gate.Release(); }
    }
    private async Task MutateAsync(Action<CommerceData> mutation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try { var data = await ReadCoreAsync(cancellationToken); mutation(data); await WriteCoreAsync(data, cancellationToken); }
        finally { _gate.Release(); }
    }
    private async Task<CommerceData> ReadCoreAsync(CancellationToken cancellationToken)
    {
        var service = await EnsureAsync(cancellationToken);
        var request = service.Spreadsheets.Values.BatchGet(settings.SpreadsheetId);
        request.Ranges = new[] { "Suppliers!A2:K", "Categories!A2:D", "Purchases!A2:P", "PurchaseItems!A2:M", "PurchaseReceipts!A2:D", "PurchaseReceiptItems!A2:K", "Intermediaries!A2:P" };
        var ranges = (await request.ExecuteAsync(cancellationToken)).ValueRanges;
        var data = new CommerceData
        {
            Suppliers = Rows(ranges, 0).Select(ParseSupplier).Where(x => x is not null).Cast<Supplier>().ToList(),
            Categories = Rows(ranges, 1).Select(ParseCategory).Where(x => x is not null).Cast<Category>().ToList(),
            Purchases = Rows(ranges, 2).Select(ParsePurchase).Where(x => x is not null).Cast<Purchase>().ToList(),
            Intermediaries = Rows(ranges, 6).Select(ParseIntermediary).Where(x => x is not null).Cast<Intermediary>().ToList()
        };
        if (data.Categories.Count == 0) data.Categories = JsonDefaultCategories();
        var purchases = data.Purchases.ToDictionary(x => x.Id);
        foreach (var row in Rows(ranges, 3))
        {
            var purchaseId = GuidValue(Cell(row, 1));
            if (purchaseId is null || !purchases.TryGetValue(purchaseId.Value, out var purchase)) continue;
            purchase.Items.Add(new PurchaseItem { Id = GuidValue(Cell(row, 0)) ?? Guid.NewGuid(), ProductId = GuidValue(Cell(row, 2)), ProductName = Cell(row, 3), Quantity = NullableInt(Cell(row, 4)), UnitPriceCny = NullableDecimal(Cell(row, 5)), UnitWeightKg = NullableDecimal(Cell(row, 6)), ProductVariantId = GuidValue(Cell(row, 7)), Color = Cell(row, 8), Size = Cell(row, 9), ReceivedQuantity = NullableInt(Cell(row, 10)), DefectQuantity = NullableInt(Cell(row, 11)), StockedQuantity = IntValue(Cell(row, 12)) });
        }
        var receipts = new Dictionary<Guid, PurchaseReceipt>();
        foreach (var row in Rows(ranges, 4))
        {
            var id = GuidValue(Cell(row, 0)); var purchaseId = GuidValue(Cell(row, 1));
            if (id is null || purchaseId is null || !purchases.TryGetValue(purchaseId.Value, out var purchase)) continue;
            var receipt = new PurchaseReceipt { Id = id.Value, ReceivedAt = DateValue(Cell(row, 2)), Note = Empty(Cell(row, 3)) };
            purchase.Receipts.Add(receipt); receipts[id.Value] = receipt;
        }
        foreach (var row in Rows(ranges, 5))
        {
            var receiptId = GuidValue(Cell(row, 1));
            if (receiptId is null || !receipts.TryGetValue(receiptId.Value, out var receipt)) continue;
            receipt.Lines.Add(new PurchaseReceiptLine { Id = GuidValue(Cell(row, 0)) ?? Guid.NewGuid(), PurchaseItemId = GuidValue(Cell(row, 2)) ?? Guid.Empty, ProductId = GuidValue(Cell(row, 3)), ProductVariantId = GuidValue(Cell(row, 4)), ProductName = Cell(row, 5), Color = Cell(row, 6), Size = Cell(row, 7), ReceivedQuantity = IntValue(Cell(row, 8)), DefectQuantity = IntValue(Cell(row, 9)), StockedQuantity = IntValue(Cell(row, 10)) });
        }
        return data;
    }
    private async Task WriteCoreAsync(CommerceData data, CancellationToken cancellationToken)
    {
        var service = await EnsureAsync(cancellationToken);
        var clear = new BatchClearValuesRequest { Ranges = ["Suppliers!A2:K", "Categories!A2:D", "Purchases!A2:P", "PurchaseItems!A2:M", "PurchaseReceipts!A2:D", "PurchaseReceiptItems!A2:K", "Intermediaries!A2:P"] };
        await service.Spreadsheets.Values.BatchClear(clear, settings.SpreadsheetId).ExecuteAsync(cancellationToken);
        var values = new List<ValueRange>();
        Add(values, "Suppliers!A2:K", data.Suppliers.Select(x => (IList<object>)[x.Id.ToString(), x.Name, x.Platform, x.StoreUrl ?? "", Obj(x.Rating), Obj(x.Moq), x.Contact ?? "", x.WeChat ?? "", x.Intermediary ?? "", x.Notes ?? "", x.CreatedAt.ToString("O")]).ToList());
        Add(values, "Categories!A2:D", data.Categories.Select(x => (IList<object>)[x.Id.ToString(), x.Name, Obj(x.SortOrder), x.IsActive]).ToList());
        Add(values, "Purchases!A2:P", data.Purchases.Select(x => (IList<object>)[x.Id.ToString(), x.Number, x.SupplierId?.ToString() ?? "", x.SupplierName ?? "", x.Status?.ToString() ?? "", x.OrderedAt.ToString("O"), Obj(x.CnyRateUzs), Obj(x.AgentCommissionPercent), Obj(x.InternationalShippingUzs), Obj(x.OtherCostsUzs), x.Notes ?? "", x.TrackingCode ?? "", x.EstimatedDeliveryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", x.IntermediaryId?.ToString() ?? "", x.IntermediaryName ?? "", CurrencyCodes.Normalize(x.CurrencyCode, "CNY")]).ToList());
        Add(values, "PurchaseItems!A2:M", data.Purchases.SelectMany(p => p.Items.Select(x => (IList<object>)[x.Id.ToString(), p.Id.ToString(), x.ProductId?.ToString() ?? "", x.ProductName, Obj(x.Quantity), Obj(x.UnitPriceCny), Obj(x.UnitWeightKg), x.ProductVariantId?.ToString() ?? "", x.Color, x.Size, Obj(x.ReceivedQuantity), Obj(x.DefectQuantity), x.StockedQuantity])).ToList());
        Add(values, "PurchaseReceipts!A2:D", data.Purchases.SelectMany(p => p.Receipts.Select(x => (IList<object>)[x.Id.ToString(), p.Id.ToString(), x.ReceivedAt.ToString("O"), x.Note ?? ""])).ToList());
        Add(values, "PurchaseReceiptItems!A2:K", data.Purchases.SelectMany(p => p.Receipts.SelectMany(r => r.Lines.Select(x => (IList<object>)[x.Id.ToString(), r.Id.ToString(), x.PurchaseItemId.ToString(), x.ProductId?.ToString() ?? "", x.ProductVariantId?.ToString() ?? "", x.ProductName, x.Color, x.Size, x.ReceivedQuantity, x.DefectQuantity, x.StockedQuantity]))).ToList());
        Add(values, "Intermediaries!A2:P", data.Intermediaries.Select(x => (IList<object>)[x.Id.ToString(), x.Name, x.Company ?? "", x.ChinaWarehouseAddress ?? "", x.ContactName ?? "", x.Telegram ?? "", x.WeChat ?? "", x.Phone ?? "", Obj(x.RatePerKgUsd), Obj(x.CommissionPercent), Obj(x.MinimumWeightKg), Obj(x.EstimatedDays), x.OfficialImport, x.Notes ?? "", x.CreatedAt.ToString("O"), Obj(x.Rating)]).ToList());
        if (values.Count > 0) await service.Spreadsheets.Values.BatchUpdate(new BatchUpdateValuesRequest { ValueInputOption = "RAW", Data = values }, settings.SpreadsheetId).ExecuteAsync(cancellationToken);
    }
    private async Task<SheetsService> EnsureAsync(CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured) throw new GoogleSheetsConfigurationException("Google Sheets не настроен.");
        if (_version != settings.Version) { _service?.Dispose(); _service = null; _initialized = false; _version = settings.Version; }
        _service ??= new SheetsService(new BaseClientService.Initializer { HttpClientInitializer = CredentialFactory.FromFile<ServiceAccountCredential>(settings.CredentialsFullPath).ToGoogleCredential().CreateScoped(SheetsService.Scope.Spreadsheets), ApplicationName = "Dona CRM" });
        if (_initialized) return _service;
        var spreadsheet = await _service.Spreadsheets.Get(settings.SpreadsheetId).ExecuteAsync(cancellationToken);
        var existing = spreadsheet.Sheets.Select(x => x.Properties.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = SheetNames.Where(x => !existing.Contains(x)).ToList();
        if (missing.Count > 0) await _service.Spreadsheets.BatchUpdate(new BatchUpdateSpreadsheetRequest { Requests = missing.Select(x => new Request { AddSheet = new AddSheetRequest { Properties = new SheetProperties { Title = x } } }).ToList() }, settings.SpreadsheetId).ExecuteAsync(cancellationToken);
        var headerData = SheetNames.Select((name, index) => new ValueRange { Range = $"{name}!A1:{ColumnName(Headers[index].Length)}1", Values = [Headers[index].Cast<object>().ToList()] }).ToList();
        await _service.Spreadsheets.Values.BatchUpdate(new BatchUpdateValuesRequest { ValueInputOption = "RAW", Data = headerData }, settings.SpreadsheetId).ExecuteAsync(cancellationToken);
        _initialized = true;
        return _service;
    }
    private static Supplier? ParseSupplier(IList<object> r) { var id = GuidValue(Cell(r, 0)); return id is null ? null : new Supplier { Id = id.Value, Name = Cell(r, 1), Platform = Cell(r, 2), StoreUrl = Empty(Cell(r, 3)), Rating = NullableDecimal(Cell(r, 4)), Moq = NullableInt(Cell(r, 5)), Contact = Empty(Cell(r, 6)), WeChat = Empty(Cell(r, 7)), Intermediary = Empty(Cell(r, 8)), Notes = Empty(Cell(r, 9)), CreatedAt = DateValue(Cell(r, 10)) }; }
    private static Category? ParseCategory(IList<object> r) { var id = GuidValue(Cell(r, 0)); return id is null ? null : new Category { Id = id.Value, Name = Cell(r, 1), SortOrder = NullableInt(Cell(r, 2)), IsActive = bool.TryParse(Cell(r, 3), out var active) && active }; }
    private static Purchase? ParsePurchase(IList<object> r) { var id = GuidValue(Cell(r, 0)); return id is null ? null : new Purchase { Id = id.Value, Number = Cell(r, 1), SupplierId = GuidValue(Cell(r, 2)), SupplierName = Empty(Cell(r, 3)), Status = Enum.TryParse<PurchaseStatus>(Cell(r, 4), true, out var status) ? status : null, OrderedAt = DateValue(Cell(r, 5)), CnyRateUzs = NullableDecimal(Cell(r, 6)), AgentCommissionPercent = NullableDecimal(Cell(r, 7)), InternationalShippingUzs = NullableDecimal(Cell(r, 8)), OtherCostsUzs = NullableDecimal(Cell(r, 9)), Notes = Empty(Cell(r, 10)), TrackingCode = Empty(Cell(r, 11)), EstimatedDeliveryDate = NullableDate(Cell(r, 12)), IntermediaryId = GuidValue(Cell(r, 13)), IntermediaryName = Empty(Cell(r, 14)), CurrencyCode = CurrencyCodes.Normalize(Cell(r, 15), "CNY") }; }
    private static Intermediary? ParseIntermediary(IList<object> r) { var id = GuidValue(Cell(r, 0)); return id is null ? null : new Intermediary { Id = id.Value, Name = Cell(r, 1), Company = Empty(Cell(r, 2)), ChinaWarehouseAddress = Empty(Cell(r, 3)), ContactName = Empty(Cell(r, 4)), Telegram = Empty(Cell(r, 5)), WeChat = Empty(Cell(r, 6)), Phone = Empty(Cell(r, 7)), RatePerKgUsd = NullableDecimal(Cell(r, 8)), CommissionPercent = NullableDecimal(Cell(r, 9)), MinimumWeightKg = NullableDecimal(Cell(r, 10)), EstimatedDays = NullableInt(Cell(r, 11)), OfficialImport = bool.TryParse(Cell(r, 12), out var official) && official, Notes = Empty(Cell(r, 13)), CreatedAt = DateValue(Cell(r, 14)), Rating = NullableDecimal(Cell(r, 15)) }; }
    private static IList<IList<object>> Rows(IList<ValueRange> ranges, int index) => index < ranges.Count ? ranges[index].Values ?? [] : [];
    private static void Add(List<ValueRange> values, string range, IList<IList<object>> rows) { if (rows.Count > 0) values.Add(new ValueRange { Range = range, Values = rows }); }
    private static void Upsert<T>(List<T> values, T value, Func<T, Guid> id) { var index = values.FindIndex(x => id(x) == id(value)); if (index >= 0) values[index] = value; else values.Add(value); }
    private static string Cell(IList<object> row, int i) => i < row.Count ? Convert.ToString(row[i], CultureInfo.InvariantCulture)?.Trim() ?? "" : "";
    private static Guid? GuidValue(string value) => Guid.TryParse(value, out var x) ? x : null;
    private static int IntValue(string value) => int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var x) ? x : 0;
    private static decimal DecimalValue(string value) => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var x) ? x : 0;
    private static decimal? NullableDecimal(string value) => string.IsNullOrWhiteSpace(value) ? null : DecimalValue(value);
    private static int? NullableInt(string value) => string.IsNullOrWhiteSpace(value) ? null : IntValue(value);
    private static object Obj<T>(T? value) where T : struct => value.HasValue ? value.Value : "";
    private static DateTimeOffset DateValue(string value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var x) ? x : DateTimeOffset.UtcNow;
    private static DateTime? NullableDate(string value) => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var x) ? x : null;
    private static string? Empty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static string ColumnName(int count) => ((char)('A' + count - 1)).ToString();
    private static List<Category> JsonDefaultCategories() => new[] { "Футболка", "Худи", "Рубашка", "Брюки", "Джинсы", "Куртка", "Сумка", "Кепка", "Ремень", "Украшения", "Другое" }.Select((x, i) => new Category { Name = x, SortOrder = i, IsActive = true }).ToList();
    public void Dispose() { _service?.Dispose(); _gate.Dispose(); }
}
