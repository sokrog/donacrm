using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Web.Services;

public sealed class DonaSyncSnapshot
{
    public List<Product> Products { get; set; } = [];
    public List<Supplier> Suppliers { get; set; } = [];
    public List<Intermediary> Intermediaries { get; set; } = [];
    public List<Category> Categories { get; set; } = [];
    public List<Purchase> Purchases { get; set; } = [];
    public List<Customer> Customers { get; set; } = [];
    public List<Sale> Sales { get; set; } = [];
    public MarketingData Marketing { get; set; } = new();
    public BusinessSettings BusinessSettings { get; set; } = new();
    public List<StockMovement> StockMovements { get; set; } = [];
    public PurchaseHistoryData PurchaseHistory { get; set; } = new();
}

public sealed record GoogleSyncEnvelope(string Version, DateTimeOffset CapturedAt, DonaSyncSnapshot Snapshot);
public sealed record GoogleSyncSection(string Key, string Title, int LocalCount, int GoogleCount);
public sealed record GoogleSyncPreview(string LocalVersion, string GoogleVersion, DateTimeOffset CapturedAt, IReadOnlyList<GoogleSyncSection> Sections)
{
    public int LocalTotal => Sections.Sum(section => section.LocalCount);
    public int GoogleTotal => Sections.Sum(section => section.GoogleCount);
}

public interface IGoogleSyncService
{
    Task<GoogleSyncPreview> PreviewAsync(CancellationToken cancellationToken = default);
    Task<GoogleSyncPreview> PullAsync(string expectedGoogleVersion, CancellationToken cancellationToken = default);
}

public interface IGoogleAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

public static class DonaSyncFingerprint
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Create(DonaSyncSnapshot snapshot)
    {
        var normalized = new DonaSyncSnapshot
        {
            Products = snapshot.Products.OrderBy(value => value.Id).ToList(),
            Suppliers = snapshot.Suppliers.OrderBy(value => value.Id).ToList(),
            Intermediaries = snapshot.Intermediaries.OrderBy(value => value.Id).ToList(),
            Categories = snapshot.Categories.OrderBy(value => value.Id).ToList(),
            Purchases = snapshot.Purchases.OrderBy(value => value.Id).ToList(),
            Customers = snapshot.Customers.OrderBy(value => value.Id).ToList(),
            Sales = snapshot.Sales.OrderBy(value => value.Id).ToList(),
            Marketing = new MarketingData
            {
                Collections = snapshot.Marketing.Collections.OrderBy(value => value.Id).ToList(),
                Outfits = snapshot.Marketing.Outfits.OrderBy(value => value.Id).ToList(),
                ContentPosts = snapshot.Marketing.ContentPosts.OrderBy(value => value.Id).ToList()
            },
            BusinessSettings = snapshot.BusinessSettings,
            StockMovements = snapshot.StockMovements.OrderBy(value => value.Id).ToList(),
            PurchaseHistory = new PurchaseHistoryData
            {
                ProductCosts = snapshot.PurchaseHistory.ProductCosts.OrderBy(value => value.Id).ToList(),
                ExchangeRates = snapshot.PurchaseHistory.ExchangeRates.OrderBy(value => value.Id).ToList()
            }
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(normalized, JsonOptions)))).ToLowerInvariant();
    }

    public static IReadOnlyList<GoogleSyncSection> Compare(DonaSyncSnapshot local, DonaSyncSnapshot google) =>
    [
        new("products", "Товары и категории", local.Products.Count + local.Categories.Count, google.Products.Count + google.Categories.Count),
        new("partners", "Партнёры", local.Suppliers.Count + local.Intermediaries.Count, google.Suppliers.Count + google.Intermediaries.Count),
        new("purchases", "Закупки", local.Purchases.Count + local.PurchaseHistory.ProductCosts.Count + local.PurchaseHistory.ExchangeRates.Count, google.Purchases.Count + google.PurchaseHistory.ProductCosts.Count + google.PurchaseHistory.ExchangeRates.Count),
        new("sales", "Клиенты и продажи", local.Customers.Count + local.Sales.Count, google.Customers.Count + google.Sales.Count),
        new("marketing", "Маркетинг", local.Marketing.Collections.Count + local.Marketing.Outfits.Count + local.Marketing.ContentPosts.Count, google.Marketing.Collections.Count + google.Marketing.Outfits.Count + google.Marketing.ContentPosts.Count),
        new("movements", "Движения склада", local.StockMovements.Count, google.StockMovements.Count)
    ];
}
