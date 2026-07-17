using System.Globalization;
using Dona.Crm.Web.Domain;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace Dona.Crm.Web.Storage;

public sealed class GoogleSheetsMarketingRepository(GoogleSheetsSettingsStore settings) : IMarketingRepository, IDisposable
{
    private static readonly string[] SheetNames = ["Collections", "CollectionProducts", "Outfits", "OutfitProducts", "ContentPlan"];
    private static readonly string[][] Headers =
    [
        ["Id", "Name", "Description", "Season", "Style", "BudgetLimitUzs", "Status", "CreatedAt"],
        ["Id", "CollectionId", "ProductId", "ProductName", "SortOrder"],
        ["Id", "Name", "Description", "Occasion", "Status", "CreatedAt"],
        ["Id", "OutfitId", "ProductId", "ProductName", "SellingPriceUzs", "SortOrder"],
        ["Id", "Title", "Type", "Status", "ScheduledAt", "CollectionId", "CollectionName", "OutfitId", "OutfitName", "Caption", "PublicationUrl", "Notes", "CreatedAt"]
    ];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SheetsService? _service;
    private int _version = -1;
    private bool _initialized;

    public Task<MarketingData> GetDataAsync(CancellationToken token = default) => ReadAsync(token);
    public async Task<IReadOnlyList<ProductCollection>> GetCollectionsAsync(CancellationToken token = default) => (await ReadAsync(token)).Collections;
    public async Task<IReadOnlyList<Outfit>> GetOutfitsAsync(CancellationToken token = default) => (await ReadAsync(token)).Outfits;
    public async Task<IReadOnlyList<ContentPost>> GetContentPostsAsync(CancellationToken token = default) => (await ReadAsync(token)).ContentPosts;
    public Task UpsertCollectionAsync(ProductCollection value, CancellationToken token = default) => MutateAsync(x => Upsert(x.Collections, value, y => y.Id), token);
    public Task DeleteCollectionAsync(Guid id, CancellationToken token = default) => MutateAsync(x => { x.Collections.RemoveAll(y => y.Id == id); foreach (var post in x.ContentPosts.Where(y => y.CollectionId == id)) { post.CollectionId = null; post.CollectionName = null; } }, token);
    public Task UpsertOutfitAsync(Outfit value, CancellationToken token = default) => MutateAsync(x => Upsert(x.Outfits, value, y => y.Id), token);
    public Task DeleteOutfitAsync(Guid id, CancellationToken token = default) => MutateAsync(x => { x.Outfits.RemoveAll(y => y.Id == id); foreach (var post in x.ContentPosts.Where(y => y.OutfitId == id)) { post.OutfitId = null; post.OutfitName = null; } }, token);
    public Task UpsertContentPostAsync(ContentPost value, CancellationToken token = default) => MutateAsync(x => Upsert(x.ContentPosts, value, y => y.Id), token);
    public Task DeleteContentPostAsync(Guid id, CancellationToken token = default) => MutateAsync(x => x.ContentPosts.RemoveAll(y => y.Id == id), token);

    private async Task<MarketingData> ReadAsync(CancellationToken token) { await _gate.WaitAsync(token); try { return await ReadCoreAsync(token); } finally { _gate.Release(); } }
    private async Task MutateAsync(Action<MarketingData> action, CancellationToken token) { await _gate.WaitAsync(token); try { var data = await ReadCoreAsync(token); action(data); await WriteCoreAsync(data, token); } finally { _gate.Release(); } }
    private async Task<MarketingData> ReadCoreAsync(CancellationToken token)
    {
        var service = await EnsureAsync(token);
        var request = service.Spreadsheets.Values.BatchGet(settings.SpreadsheetId);
        request.Ranges = new[] { "Collections!A2:H", "CollectionProducts!A2:E", "Outfits!A2:F", "OutfitProducts!A2:F", "ContentPlan!A2:M" };
        var ranges = (await request.ExecuteAsync(token)).ValueRanges;
        var data = new MarketingData
        {
            Collections = Rows(ranges, 0).Select(ParseCollection).Where(x => x is not null).Cast<ProductCollection>().ToList(),
            Outfits = Rows(ranges, 2).Select(ParseOutfit).Where(x => x is not null).Cast<Outfit>().ToList(),
            ContentPosts = Rows(ranges, 4).Select(ParsePost).Where(x => x is not null).Cast<ContentPost>().ToList()
        };
        var collections = data.Collections.ToDictionary(x => x.Id);
        foreach (var row in Rows(ranges, 1)) { var parentId = GuidValue(Cell(row, 1)); if (parentId is not null && collections.TryGetValue(parentId.Value, out var parent)) parent.Products.Add(new CollectionProduct { Id = GuidValue(Cell(row, 0)) ?? Guid.NewGuid(), ProductId = GuidValue(Cell(row, 2)), ProductName = Cell(row, 3), SortOrder = IntValue(Cell(row, 4)) }); }
        var outfits = data.Outfits.ToDictionary(x => x.Id);
        foreach (var row in Rows(ranges, 3)) { var parentId = GuidValue(Cell(row, 1)); if (parentId is not null && outfits.TryGetValue(parentId.Value, out var parent)) parent.Products.Add(new OutfitProduct { Id = GuidValue(Cell(row, 0)) ?? Guid.NewGuid(), ProductId = GuidValue(Cell(row, 2)), ProductName = Cell(row, 3), SellingPriceUzs = NullableDecimal(Cell(row, 4)), SortOrder = IntValue(Cell(row, 5)) }); }
        foreach (var collection in data.Collections) collection.Products = collection.Products.OrderBy(x => x.SortOrder).ToList();
        foreach (var outfit in data.Outfits) outfit.Products = outfit.Products.OrderBy(x => x.SortOrder).ToList();
        return data;
    }
    private async Task WriteCoreAsync(MarketingData data, CancellationToken token)
    {
        var service = await EnsureAsync(token);
        await service.Spreadsheets.Values.BatchClear(new BatchClearValuesRequest { Ranges = ["Collections!A2:H", "CollectionProducts!A2:E", "Outfits!A2:F", "OutfitProducts!A2:F", "ContentPlan!A2:M"] }, settings.SpreadsheetId).ExecuteAsync(token);
        var values = new List<ValueRange>();
        Add(values, "Collections!A2:H", data.Collections.Select(x => (IList<object>)[x.Id.ToString(), x.Name, x.Description ?? "", x.Season ?? "", x.Style ?? "", Obj(x.BudgetLimitUzs), x.Status?.ToString() ?? "", x.CreatedAt.ToString("O")]).ToList());
        Add(values, "CollectionProducts!A2:E", data.Collections.SelectMany(c => c.Products.Select(x => (IList<object>)[x.Id.ToString(), c.Id.ToString(), x.ProductId?.ToString() ?? "", x.ProductName, x.SortOrder])).ToList());
        Add(values, "Outfits!A2:F", data.Outfits.Select(x => (IList<object>)[x.Id.ToString(), x.Name, x.Description ?? "", x.Occasion ?? "", x.Status?.ToString() ?? "", x.CreatedAt.ToString("O")]).ToList());
        Add(values, "OutfitProducts!A2:F", data.Outfits.SelectMany(o => o.Products.Select(x => (IList<object>)[x.Id.ToString(), o.Id.ToString(), x.ProductId?.ToString() ?? "", x.ProductName, Obj(x.SellingPriceUzs), x.SortOrder])).ToList());
        Add(values, "ContentPlan!A2:M", data.ContentPosts.Select(x => (IList<object>)[x.Id.ToString(), x.Title, x.Type?.ToString() ?? "", x.Status?.ToString() ?? "", x.ScheduledAt?.ToString("O") ?? "", x.CollectionId?.ToString() ?? "", x.CollectionName ?? "", x.OutfitId?.ToString() ?? "", x.OutfitName ?? "", x.Caption ?? "", x.PublicationUrl ?? "", x.Notes ?? "", x.CreatedAt.ToString("O")]).ToList());
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
        var headers = SheetNames.Select((name, i) => new ValueRange { Range = $"{name}!A1:{ColumnName(Headers[i].Length)}1", Values = [Headers[i].Cast<object>().ToList()] }).ToList();
        await _service.Spreadsheets.Values.BatchUpdate(new BatchUpdateValuesRequest { ValueInputOption = "RAW", Data = headers }, settings.SpreadsheetId).ExecuteAsync(token);
        _initialized = true;
        return _service;
    }
    private static ProductCollection? ParseCollection(IList<object> r) { var id = GuidValue(Cell(r, 0)); return id is null ? null : new ProductCollection { Id = id.Value, Name = Cell(r, 1), Description = Empty(Cell(r, 2)), Season = Empty(Cell(r, 3)), Style = Empty(Cell(r, 4)), BudgetLimitUzs = NullableDecimal(Cell(r, 5)), Status = Enum.TryParse<MarketingStatus>(Cell(r, 6), true, out var status) ? status : null, CreatedAt = DateValue(Cell(r, 7)) }; }
    private static Outfit? ParseOutfit(IList<object> r) { var id = GuidValue(Cell(r, 0)); return id is null ? null : new Outfit { Id = id.Value, Name = Cell(r, 1), Description = Empty(Cell(r, 2)), Occasion = Empty(Cell(r, 3)), Status = Enum.TryParse<MarketingStatus>(Cell(r, 4), true, out var status) ? status : null, CreatedAt = DateValue(Cell(r, 5)) }; }
    private static ContentPost? ParsePost(IList<object> r) { var id = GuidValue(Cell(r, 0)); return id is null ? null : new ContentPost { Id = id.Value, Title = Cell(r, 1), Type = Enum.TryParse<ContentType>(Cell(r, 2), true, out var type) ? type : null, Status = Enum.TryParse<ContentStatus>(Cell(r, 3), true, out var status) ? status : null, ScheduledAt = NullableDate(Cell(r, 4)), CollectionId = GuidValue(Cell(r, 5)), CollectionName = Empty(Cell(r, 6)), OutfitId = GuidValue(Cell(r, 7)), OutfitName = Empty(Cell(r, 8)), Caption = Empty(Cell(r, 9)), PublicationUrl = Empty(Cell(r, 10)), Notes = Empty(Cell(r, 11)), CreatedAt = DateValue(Cell(r, 12)) }; }
    private static IList<IList<object>> Rows(IList<ValueRange> ranges, int i) => i < ranges.Count ? ranges[i].Values ?? [] : [];
    private static void Add(List<ValueRange> values, string range, IList<IList<object>> rows) { if (rows.Count > 0) values.Add(new ValueRange { Range = range, Values = rows }); }
    private static void Upsert<T>(List<T> list, T item, Func<T, Guid> id) { var i = list.FindIndex(x => id(x) == id(item)); if (i >= 0) list[i] = item; else list.Add(item); }
    private static string Cell(IList<object> row, int i) => i < row.Count ? Convert.ToString(row[i], CultureInfo.InvariantCulture)?.Trim() ?? "" : "";
    private static Guid? GuidValue(string value) => Guid.TryParse(value, out var x) ? x : null;
    private static int IntValue(string value) => int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var x) ? x : 0;
    private static decimal? NullableDecimal(string value) => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var x) ? x : null;
    private static DateTimeOffset DateValue(string value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var x) ? x : DateTimeOffset.UtcNow;
    private static DateTime? NullableDate(string value) => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var x) ? x : null;
    private static string? Empty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static object Obj<T>(T? value) where T : struct => value.HasValue ? value.Value : "";
    private static string ColumnName(int count) => ((char)('A' + count - 1)).ToString();
    public void Dispose() { _service?.Dispose(); _gate.Dispose(); }
}
