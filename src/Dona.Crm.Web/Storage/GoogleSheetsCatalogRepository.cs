using System.Globalization;
using Dona.Crm.Web.Domain;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace Dona.Crm.Web.Storage;

public sealed class GoogleSheetsCatalogRepository : ICatalogRepository, IDisposable
{
    public const string ProductsSheet = "Products";
    public const string VariantsSheet = "ProductVariants";
    public const string ImagesSheet = "ProductImages";
    private static readonly string[] ProductHeaders = ["Id", "Sku", "Name", "Category", "Gender", "Season", "Status", "SupplierName", "SourceUrl", "ImageUrl", "Notes", "PurchasePriceCny", "CnyRateUzs", "AgentCommissionPercent", "DeliveryCostUzs", "SellingPriceUzs", "CreatedAt"];
    private static readonly string[] VariantHeaders = ["Id", "ProductId", "Color", "Size", "Quantity", "ReservedQuantity"];
    private static readonly string[] ImageHeaders = ["Id", "ProductId", "FileName", "ContentType", "SizeBytes", "Storage", "StorageKey", "Url", "Caption", "SortOrder", "IsMain", "CreatedAt"];
    private readonly GoogleSheetsSettingsStore _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SheetsService? _service;
    private bool _initialized;
    private int _settingsVersion = -1;

    public GoogleSheetsCatalogRepository(GoogleSheetsSettingsStore settings) => _settings = settings;

    public bool IsConfigured => _settings.IsConfigured;
    public string SpreadsheetId => _settings.SpreadsheetId;
    public string CredentialsFullPath => _settings.CredentialsFullPath;

    public async Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await ReadCoreAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task<Product?> GetProductAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await GetProductsAsync(cancellationToken)).FirstOrDefault(x => x.Id == id);

    public async Task UpsertProductAsync(Product product, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var products = await ReadCoreAsync(cancellationToken);
            var index = products.FindIndex(x => x.Id == product.Id);
            if (index >= 0) products[index] = product; else products.Add(product);
            await WriteCoreAsync(products, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteProductAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var products = await ReadCoreAsync(cancellationToken);
            products.RemoveAll(x => x.Id == id);
            await WriteCoreAsync(products, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task ReplaceAllAsync(IEnumerable<Product> products, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { await WriteCoreAsync(products.ToList(), cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task CheckConnectionAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { await EnsureInitializedAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    private async Task<List<Product>> ReadCoreAsync(CancellationToken cancellationToken)
    {
        var service = await EnsureInitializedAsync(cancellationToken);
        var request = service.Spreadsheets.Values.BatchGet(SpreadsheetId);
        request.Ranges = new[] { $"{ProductsSheet}!A2:Q", $"{VariantsSheet}!A2:F", $"{ImagesSheet}!A2:L" };
        var response = await request.ExecuteAsync(cancellationToken);
        var productRows = response.ValueRanges.ElementAtOrDefault(0)?.Values ?? [];
        var variantRows = response.ValueRanges.ElementAtOrDefault(1)?.Values ?? [];
        var imageRows = response.ValueRanges.ElementAtOrDefault(2)?.Values ?? [];

        var products = productRows.Select(ParseProduct).Where(x => x is not null).Cast<Product>().ToList();
        var byId = products.ToDictionary(x => x.Id);
        foreach (var row in variantRows)
        {
            var productId = ParseGuid(Cell(row, 1));
            if (productId is null || !byId.TryGetValue(productId.Value, out var product)) continue;
            product.Variants.Add(new ProductVariant
            {
                Id = ParseGuid(Cell(row, 0)) ?? Guid.NewGuid(),
                Color = Cell(row, 2),
                Size = Cell(row, 3),
                Quantity = ParseInt(Cell(row, 4)),
                ReservedQuantity = ParseInt(Cell(row, 5))
            });
        }
        foreach (var row in imageRows)
        {
            var productId = ParseGuid(Cell(row, 1));
            if (productId is null || !byId.TryGetValue(productId.Value, out var product)) continue;
            product.Images.Add(new ProductImage { Id = ParseGuid(Cell(row, 0)) ?? Guid.NewGuid(), FileName = Cell(row, 2), ContentType = Cell(row, 3), SizeBytes = ParseLong(Cell(row, 4)), Storage = Enum.TryParse<ProductImageStorage>(Cell(row, 5), true, out var storage) ? storage : ProductImageStorage.External, StorageKey = Cell(row, 6), Url = Cell(row, 7), Caption = NullIfEmpty(Cell(row, 8)), SortOrder = ParseInt(Cell(row, 9)), IsMain = bool.TryParse(Cell(row, 10), out var main) && main, CreatedAt = DateTimeOffset.TryParse(Cell(row, 11), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var created) ? created : DateTimeOffset.UtcNow });
        }
        return products;
    }

    private async Task WriteCoreAsync(List<Product> products, CancellationToken cancellationToken)
    {
        var service = await EnsureInitializedAsync(cancellationToken);
        var clear = new BatchClearValuesRequest { Ranges = [$"{ProductsSheet}!A2:Q", $"{VariantsSheet}!A2:F", $"{ImagesSheet}!A2:L"] };
        await service.Spreadsheets.Values.BatchClear(clear, SpreadsheetId).ExecuteAsync(cancellationToken);

        var productRows = products.Select(ToProductRow).ToList();
        var variantRows = products.SelectMany(product => product.Variants.Select(variant => ToVariantRow(product.Id, variant))).ToList();
        var imageRows = products.SelectMany(product => product.Images.Select(image => ToImageRow(product.Id, image))).ToList();
        var data = new List<ValueRange>();
        if (productRows.Count > 0) data.Add(new ValueRange { Range = $"{ProductsSheet}!A2:Q", Values = productRows });
        if (variantRows.Count > 0) data.Add(new ValueRange { Range = $"{VariantsSheet}!A2:F", Values = variantRows });
        if (imageRows.Count > 0) data.Add(new ValueRange { Range = $"{ImagesSheet}!A2:L", Values = imageRows });
        if (data.Count == 0) return;
        var update = new BatchUpdateValuesRequest { ValueInputOption = "RAW", Data = data };
        await service.Spreadsheets.Values.BatchUpdate(update, SpreadsheetId).ExecuteAsync(cancellationToken);
    }

    private async Task<SheetsService> EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        ValidateConfiguration();
        if (_settingsVersion != _settings.Version)
        {
            _service?.Dispose();
            _service = null;
            _initialized = false;
            _settingsVersion = _settings.Version;
        }
        _service ??= CreateService();
        if (_initialized) return _service;

        var spreadsheet = await _service.Spreadsheets.Get(SpreadsheetId).ExecuteAsync(cancellationToken);
        var existing = spreadsheet.Sheets.Select(x => x.Properties.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = new[] { ProductsSheet, VariantsSheet, ImagesSheet }.Where(x => !existing.Contains(x)).ToList();
        if (missing.Count > 0)
        {
            var batch = new BatchUpdateSpreadsheetRequest
            {
                Requests = missing.Select(name => new Request { AddSheet = new AddSheetRequest { Properties = new SheetProperties { Title = name } } }).ToList()
            };
            await _service.Spreadsheets.BatchUpdate(batch, SpreadsheetId).ExecuteAsync(cancellationToken);
        }

        var headers = new BatchUpdateValuesRequest
        {
            ValueInputOption = "RAW",
            Data =
            [
                new ValueRange { Range = $"{ProductsSheet}!A1:Q1", Values = [ProductHeaders.Cast<object>().ToList()] },
                new ValueRange { Range = $"{VariantsSheet}!A1:F1", Values = [VariantHeaders.Cast<object>().ToList()] },
                new ValueRange { Range = $"{ImagesSheet}!A1:L1", Values = [ImageHeaders.Cast<object>().ToList()] }
            ]
        };
        await _service.Spreadsheets.Values.BatchUpdate(headers, SpreadsheetId).ExecuteAsync(cancellationToken);
        _initialized = true;
        return _service;
    }

    private SheetsService CreateService()
    {
        var credential = CredentialFactory.FromFile<ServiceAccountCredential>(CredentialsFullPath)
            .ToGoogleCredential()
            .CreateScoped(SheetsService.Scope.Spreadsheets);
        return new SheetsService(new BaseClientService.Initializer { HttpClientInitializer = credential, ApplicationName = "Dona CRM" });
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(SpreadsheetId)) throw new GoogleSheetsConfigurationException("Не указан ID Google-таблицы.");
        if (!File.Exists(CredentialsFullPath)) throw new GoogleSheetsConfigurationException($"Файл сервисного аккаунта не найден: {CredentialsFullPath}");
    }

    private static Product? ParseProduct(IList<object> row)
    {
        var id = ParseGuid(Cell(row, 0));
        if (id is null) return null;
        return new Product
        {
            Id = id.Value, Sku = Cell(row, 1), Name = Cell(row, 2), Category = Cell(row, 3), Gender = Cell(row, 4), Season = Cell(row, 5),
            Status = Enum.TryParse<ProductStatus>(Cell(row, 6), true, out var status) ? status : null,
            SupplierName = NullIfEmpty(Cell(row, 7)), SourceUrl = NullIfEmpty(Cell(row, 8)), ImageUrl = NullIfEmpty(Cell(row, 9)), Notes = NullIfEmpty(Cell(row, 10)),
            PurchasePriceCny = ParseNullableDecimal(Cell(row, 11)), CnyRateUzs = ParseNullableDecimal(Cell(row, 12)), AgentCommissionPercent = ParseNullableDecimal(Cell(row, 13)),
            DeliveryCostUzs = ParseNullableDecimal(Cell(row, 14)), SellingPriceUzs = ParseNullableDecimal(Cell(row, 15)),
            CreatedAt = DateTimeOffset.TryParse(Cell(row, 16), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) ? date : DateTimeOffset.UtcNow
        };
    }

    private static IList<object> ToProductRow(Product x) => [x.Id.ToString(), x.Sku, x.Name, x.Category, x.Gender, x.Season, x.Status?.ToString() ?? "", x.SupplierName ?? "", x.SourceUrl ?? "", x.ImageUrl ?? "", x.Notes ?? "", Obj(x.PurchasePriceCny), Obj(x.CnyRateUzs), Obj(x.AgentCommissionPercent), Obj(x.DeliveryCostUzs), Obj(x.SellingPriceUzs), x.CreatedAt.ToString("O")];
    private static IList<object> ToVariantRow(Guid productId, ProductVariant x) => [x.Id.ToString(), productId.ToString(), x.Color, x.Size, Obj(x.Quantity), x.ReservedQuantity];
    private static IList<object> ToImageRow(Guid productId, ProductImage x) => [x.Id.ToString(), productId.ToString(), x.FileName, x.ContentType, x.SizeBytes, x.Storage.ToString(), x.StorageKey, x.Url, x.Caption ?? "", x.SortOrder, x.IsMain, x.CreatedAt.ToString("O")];
    private static string Cell(IList<object> row, int index) => index < row.Count ? Convert.ToString(row[index], CultureInfo.InvariantCulture)?.Trim() ?? "" : "";
    private static Guid? ParseGuid(string value) => Guid.TryParse(value, out var result) ? result : null;
    private static decimal ParseDecimal(string value) => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;
    private static decimal? ParseNullableDecimal(string value) => string.IsNullOrWhiteSpace(value) ? null : ParseDecimal(value);
    private static object Obj<T>(T? value) where T : struct => value.HasValue ? value.Value : "";
    private static int ParseInt(string value) => int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;
    private static long ParseLong(string value) => long.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;
    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    public void Dispose() { _service?.Dispose(); _gate.Dispose(); }
}

public sealed class GoogleSheetsConfigurationException(string message) : InvalidOperationException(message);
