using System.Globalization;
using System.Reflection;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Web.Services;

public sealed record GoogleSheetImportIssue(string Sheet, int Row, string Message);

public sealed record GoogleSheetImportResult(
    DonaSyncSnapshot Snapshot,
    IReadOnlyList<GoogleSheetImportIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

public static class GoogleSheetSnapshotParser
{
    public static IReadOnlyList<string> SheetTitles { get; } =
    [
        "Products", "ProductVariants", "ProductImages",
        "Suppliers", "Categories", "Purchases", "PurchaseItems", "PurchaseReceipts",
        "PurchaseReceiptItems", "Intermediaries",
        "Customers", "Sales", "SaleItems", "SaleReturns", "SaleReturnItems", "Payments",
        "Collections", "CollectionProducts", "CollectionImages",
        "Outfits", "OutfitProducts", "OutfitImages", "ContentPlan",
        "AppSettings", "StockMovements", "ProductCostHistory", "ExchangeRateHistory"
    ];

    public static GoogleSheetImportResult Parse(IEnumerable<GoogleSyncSheet> sheets)
    {
        var issues = new List<GoogleSheetImportIssue>();
        var source = sheets
            .GroupBy(sheet => sheet.Title, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var products = ParseEntities<Product>(source, "Products", issues);
        Attach(source, "ProductVariants", "ProductId", products, product => product.Variants, issues);
        Attach(source, "ProductImages", "ProductId", products, product => product.Images, issues);

        var purchases = ParseEntities<Purchase>(source, "Purchases", issues);
        Attach(source, "PurchaseItems", "PurchaseId", purchases, purchase => purchase.Items, issues);
        var receipts = Attach(source, "PurchaseReceipts", "PurchaseId", purchases, purchase => purchase.Receipts, issues);
        Attach(source, "PurchaseReceiptItems", "ReceiptId", receipts, receipt => receipt.Lines, issues);

        var sales = ParseEntities<Sale>(source, "Sales", issues);
        Attach(source, "SaleItems", "SaleId", sales, sale => sale.Items, issues);
        var returns = Attach(source, "SaleReturns", "SaleId", sales, sale => sale.Returns, issues);
        Attach(source, "SaleReturnItems", "ReturnId", returns, saleReturn => saleReturn.Items, issues);
        Attach(source, "Payments", "SaleId", sales, sale => sale.Payments, issues);

        var collections = ParseEntities<ProductCollection>(source, "Collections", issues);
        Attach(source, "CollectionProducts", "CollectionId", collections, collection => collection.Products, issues);
        Attach(source, "CollectionImages", "CollectionId", collections, collection => collection.Images, issues);
        var outfits = ParseEntities<Outfit>(source, "Outfits", issues);
        Attach(source, "OutfitProducts", "OutfitId", outfits, outfit => outfit.Products, issues);
        Attach(source, "OutfitImages", "OutfitId", outfits, outfit => outfit.Images, issues);

        var snapshot = new DonaSyncSnapshot
        {
            Products = products,
            Suppliers = ParseEntities<Supplier>(source, "Suppliers", issues),
            Intermediaries = ParseEntities<Intermediary>(source, "Intermediaries", issues),
            Categories = ParseEntities<Category>(source, "Categories", issues),
            Purchases = purchases,
            Customers = ParseEntities<Customer>(source, "Customers", issues),
            Sales = sales,
            Marketing = new MarketingData
            {
                Collections = collections,
                Outfits = outfits,
                ContentPosts = ParseEntities<ContentPost>(source, "ContentPlan", issues)
            },
            BusinessSettings = ParseSettings(source, issues),
            StockMovements = ParseEntities<StockMovement>(source, "StockMovements", issues),
            PurchaseHistory = new PurchaseHistoryData
            {
                ProductCosts = ParseEntities<ProductCostHistoryEntry>(source, "ProductCostHistory", issues),
                ExchangeRates = ParseEntities<ExchangeRateHistoryEntry>(source, "ExchangeRateHistory", issues)
            }
        };

        return new GoogleSheetImportResult(snapshot, issues);
    }

    private static List<T> ParseEntities<T>(
        IReadOnlyDictionary<string, GoogleSyncSheet> source,
        string sheetTitle,
        List<GoogleSheetImportIssue> issues)
        where T : class, new()
    {
        if (!source.TryGetValue(sheetTitle, out var sheet))
            return [];

        var entities = new List<T>();
        var ids = new HashSet<Guid>();
        for (var index = 0; index < sheet.Rows.Count; index++)
        {
            var rowNumber = index + 2;
            var row = RowValues(sheet, sheet.Rows[index]);
            if (!TryRequiredGuid(row, "Id", out var id))
            {
                issues.Add(new(sheetTitle, rowNumber, "Поле Id отсутствует или содержит неверный GUID."));
                continue;
            }

            if (!ids.Add(id))
            {
                issues.Add(new(sheetTitle, rowNumber, $"Повторяющийся Id {id}."));
                continue;
            }

            if (TryCreate(row, sheetTitle, rowNumber, issues, out T? entity) && entity is not null)
                entities.Add(entity);
        }

        return entities;
    }

    private static List<TChild> Attach<TParent, TChild>(
        IReadOnlyDictionary<string, GoogleSyncSheet> source,
        string sheetTitle,
        string parentColumn,
        IReadOnlyList<TParent> parents,
        Func<TParent, List<TChild>> children,
        List<GoogleSheetImportIssue> issues)
        where TParent : class
        where TChild : class, new()
    {
        if (!source.TryGetValue(sheetTitle, out var sheet))
            return [];

        var parentById = parents
            .Select(parent => (Parent: parent, Id: GetEntityId(parent)))
            .Where(pair => pair.Id != Guid.Empty)
            .ToDictionary(pair => pair.Id, pair => pair.Parent);
        var attached = new List<TChild>();
        var ids = new HashSet<Guid>();

        for (var index = 0; index < sheet.Rows.Count; index++)
        {
            var rowNumber = index + 2;
            var row = RowValues(sheet, sheet.Rows[index]);
            if (!TryRequiredGuid(row, "Id", out var id))
            {
                issues.Add(new(sheetTitle, rowNumber, "Поле Id отсутствует или содержит неверный GUID."));
                continue;
            }

            if (!ids.Add(id))
            {
                issues.Add(new(sheetTitle, rowNumber, $"Повторяющийся Id {id}."));
                continue;
            }

            if (!TryRequiredGuid(row, parentColumn, out var parentId) ||
                !parentById.TryGetValue(parentId, out var parent))
            {
                issues.Add(new(sheetTitle, rowNumber, $"Не найдена родительская запись {parentColumn}."));
                continue;
            }

            if (!TryCreate(row, sheetTitle, rowNumber, issues, out TChild? child) || child is null)
                continue;

            children(parent).Add(child);
            attached.Add(child);
        }

        return attached;
    }

    private static BusinessSettings ParseSettings(
        IReadOnlyDictionary<string, GoogleSyncSheet> source,
        List<GoogleSheetImportIssue> issues)
    {
        var settings = new BusinessSettings();
        if (!source.TryGetValue("AppSettings", out var sheet))
            return settings;

        var properties = typeof(BusinessSettings).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanWrite)
            .ToDictionary(property => property.Name, StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < sheet.Rows.Count; index++)
        {
            var row = RowValues(sheet, sheet.Rows[index]);
            var key = Text(row.GetValueOrDefault("Key"));
            if (string.IsNullOrWhiteSpace(key) || !properties.TryGetValue(key, out var property))
                continue;

            try
            {
                property.SetValue(settings, ConvertValue(row.GetValueOrDefault("Value"), property.PropertyType));
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException or OverflowException)
            {
                issues.Add(new("AppSettings", index + 2, $"Настройка {key}: {exception.Message}"));
            }
        }

        return settings;
    }

    private static bool TryCreate<T>(
        IReadOnlyDictionary<string, object?> row,
        string sheetTitle,
        int rowNumber,
        List<GoogleSheetImportIssue> issues,
        out T? entity)
        where T : class, new()
    {
        entity = new T();
        foreach (var property in typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(property => property.CanWrite && IsSupported(property.PropertyType)))
        {
            if (!row.TryGetValue(property.Name, out var rawValue))
                continue;

            try
            {
                property.SetValue(entity, ConvertValue(rawValue, property.PropertyType));
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException or OverflowException)
            {
                issues.Add(new(sheetTitle, rowNumber, $"{property.Name}: {exception.Message}"));
                entity = null;
                return false;
            }
        }

        return true;
    }

    private static Dictionary<string, object?> RowValues(GoogleSyncSheet sheet, IReadOnlyList<object> row)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < sheet.Headers.Count; index++)
            values[sheet.Headers[index]] = index < row.Count ? row[index] : null;
        return values;
    }

    private static Guid GetEntityId<T>(T entity)
    {
        var value = entity?.GetType().GetProperty("Id", BindingFlags.Instance | BindingFlags.Public)?.GetValue(entity);
        return value is Guid id ? id : Guid.Empty;
    }

    private static bool TryRequiredGuid(IReadOnlyDictionary<string, object?> row, string key, out Guid id)
    {
        id = Guid.Empty;
        return row.TryGetValue(key, out var value) && Guid.TryParse(Text(value), out id) && id != Guid.Empty;
    }

    private static bool IsSupported(Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        return target == typeof(string) ||
               target == typeof(Guid) ||
               target == typeof(bool) ||
               target == typeof(int) ||
               target == typeof(long) ||
               target == typeof(decimal) ||
               target == typeof(DateTime) ||
               target == typeof(DateTimeOffset) ||
               target.IsEnum;
    }

    private static object? ConvertValue(object? value, Type type)
    {
        var nullableType = Nullable.GetUnderlyingType(type);
        var target = nullableType ?? type;
        var text = Text(value);
        if (nullableType is not null && string.IsNullOrWhiteSpace(text))
            return null;

        if (target == typeof(string))
            return text;
        if (target == typeof(Guid))
            return Guid.Parse(text);
        if (target == typeof(bool))
            return bool.Parse(text);
        if (target == typeof(int))
            return int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (target == typeof(long))
            return long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (target == typeof(decimal))
            return decimal.Parse(text, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture);
        if (target == typeof(DateTime))
            return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (target == typeof(DateTimeOffset))
            return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (target.IsEnum)
            return Enum.Parse(target, text, ignoreCase: true);

        throw new ArgumentException($"Тип {target.Name} не поддерживается.");
    }

    private static string Text(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
}
