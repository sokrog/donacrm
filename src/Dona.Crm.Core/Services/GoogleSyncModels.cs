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

public sealed record GoogleSyncEnvelope(string Version, DateTimeOffset CapturedAt, DonaSyncSnapshot Snapshot, string? RemoteId = null, string? RemoteETag = null);
public sealed record GoogleSyncSection(string Key, string Title, int LocalCount, int GoogleCount);
public sealed record GoogleSyncPreview(string LocalVersion, string GoogleVersion, DateTimeOffset CapturedAt, IReadOnlyList<GoogleSyncSection> Sections)
{
    public int LocalTotal => Sections.Sum(section => section.LocalCount);
    public int GoogleTotal => Sections.Sum(section => section.GoogleCount);
}

public enum GoogleSyncOperationStatus { Prepared, Applied, RequiresRetry }

public sealed class GoogleSyncOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public GoogleSyncOperationStatus Status { get; set; } = GoogleSyncOperationStatus.Prepared;
    public string ExpectedGoogleVersion { get; set; } = string.Empty;
    public string LocalVersion { get; set; } = string.Empty;
    public DonaSyncSnapshot Snapshot { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AppliedAt { get; set; }
    public string? Error { get; set; }
}

public sealed record GoogleSyncPushRequest(
    string SpreadsheetId,
    Guid OperationId,
    string ExpectedGoogleVersion,
    string LocalVersion,
    DateTimeOffset CreatedAt,
    DonaSyncSnapshot Snapshot);

public sealed record GoogleSyncPushResult(Guid OperationId, string Version, DateTimeOffset AppliedAt, bool AlreadyApplied);

public enum GoogleSyncState { LocalOnly, Pending, Synced, Conflict, Error }

public sealed class GoogleSyncCheckpoint
{
    public string LocalVersion { get; set; } = string.Empty;
    public string GoogleVersion { get; set; } = string.Empty;
    public DateTimeOffset? LastSuccessfulAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
    public bool HasConflict { get; set; }
    public bool IsPending { get; set; }
}

public sealed record GoogleSyncStatus(
    GoogleSyncState State,
    DateTimeOffset? LastSuccessfulAt = null,
    string? Error = null);

public interface IGoogleSyncCheckpointStore
{
    Task<GoogleSyncCheckpoint?> ReadAsync(CancellationToken cancellationToken = default);
    Task WriteAsync(GoogleSyncCheckpoint checkpoint, CancellationToken cancellationToken = default);
}

public interface IGoogleSyncService
{
    Task<GoogleSyncStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<GoogleSyncPreview> PreviewAsync(CancellationToken cancellationToken = default);
    Task<GoogleSyncPreview> PullAsync(string expectedGoogleVersion, CancellationToken cancellationToken = default);
    Task<GoogleSyncPushResult> PushAsync(string expectedGoogleVersion, CancellationToken cancellationToken = default);
    Task<GoogleSyncPushResult> RetryPushAsync(Guid operationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GoogleSyncOperation>> GetOperationsAsync(CancellationToken cancellationToken = default);
}

public interface ILocalSyncResetService
{
    Task ResetLocalStateAsync(CancellationToken cancellationToken = default);
}

public sealed class GoogleSyncConflictException(string message) : InvalidOperationException(message);

public static class GoogleSyncStatusEvaluator
{
    public static GoogleSyncStatus Evaluate(bool connected, string localVersion, GoogleSyncCheckpoint? checkpoint)
    {
        if (!connected)
            return new(GoogleSyncState.LocalOnly);
        if (checkpoint?.HasConflict == true)
            return new(GoogleSyncState.Conflict, checkpoint.LastSuccessfulAt, checkpoint.LastError);
        if (!string.IsNullOrWhiteSpace(checkpoint?.LastError))
            return new(GoogleSyncState.Error, checkpoint.LastSuccessfulAt, checkpoint.LastError);
        if (checkpoint?.IsPending == true)
            return new(GoogleSyncState.Pending, checkpoint.LastSuccessfulAt);
        if (checkpoint?.LastSuccessfulAt is not null &&
            string.Equals(localVersion, checkpoint.LocalVersion, StringComparison.Ordinal))
            return new(GoogleSyncState.Synced, checkpoint.LastSuccessfulAt);
        return new(GoogleSyncState.Pending, checkpoint?.LastSuccessfulAt);
    }
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
