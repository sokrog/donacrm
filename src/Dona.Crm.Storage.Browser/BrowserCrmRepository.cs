using System.Text.Json;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;
using Dona.Crm.Web.Storage;
using Microsoft.JSInterop;

namespace Dona.Crm.Storage.Browser;

public sealed class BrowserCrmRepository(IJSRuntime javascript) :
    ICatalogRepository,
    ICommerceRepository,
    ISalesRepository,
    IMarketingRepository,
    IBusinessSettingsRepository,
    IStockMovementRepository,
    IPurchaseHistoryRepository,
    IBackupSnapshotStore
{
    private const string StorageKey = "dona.crm.browser.snapshot.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default) => (await LoadAsync(cancellationToken)).Products;
    public async Task<Product?> GetProductAsync(Guid id, CancellationToken cancellationToken = default) => (await LoadAsync(cancellationToken)).Products.FirstOrDefault(value => value.Id == id);
    public Task UpsertProductAsync(Product product, CancellationToken cancellationToken = default) => MutateAsync(snapshot => Upsert(snapshot.Products, product, value => value.Id), cancellationToken);
    public Task DeleteProductAsync(Guid id, CancellationToken cancellationToken = default) => MutateAsync(snapshot => snapshot.Products.RemoveAll(value => value.Id == id), cancellationToken);

    public async Task<IReadOnlyList<Supplier>> GetSuppliersAsync(CancellationToken cancellationToken = default) => (await LoadAsync(cancellationToken)).Suppliers;
    public Task UpsertSupplierAsync(Supplier supplier, CancellationToken cancellationToken = default) => MutateAsync(snapshot => Upsert(snapshot.Suppliers, supplier, value => value.Id), cancellationToken);
    public Task DeleteSupplierAsync(Guid id, CancellationToken cancellationToken = default) => MutateAsync(snapshot => snapshot.Suppliers.RemoveAll(value => value.Id == id), cancellationToken);
    public async Task<IReadOnlyList<Intermediary>> GetIntermediariesAsync(CancellationToken cancellationToken = default) => (await LoadAsync(cancellationToken)).Intermediaries;
    public Task UpsertIntermediaryAsync(Intermediary intermediary, CancellationToken cancellationToken = default) => MutateAsync(snapshot => Upsert(snapshot.Intermediaries, intermediary, value => value.Id), cancellationToken);
    public Task DeleteIntermediaryAsync(Guid id, CancellationToken cancellationToken = default) => MutateAsync(snapshot => snapshot.Intermediaries.RemoveAll(value => value.Id == id), cancellationToken);
    public async Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default) => (await LoadAsync(cancellationToken)).Categories;
    public Task UpsertCategoryAsync(Category category, CancellationToken cancellationToken = default) => MutateAsync(snapshot => Upsert(snapshot.Categories, category, value => value.Id), cancellationToken);
    public Task DeleteCategoryAsync(Guid id, CancellationToken cancellationToken = default) => MutateAsync(snapshot => snapshot.Categories.RemoveAll(value => value.Id == id), cancellationToken);
    public async Task<IReadOnlyList<Purchase>> GetPurchasesAsync(CancellationToken cancellationToken = default) => (await LoadAsync(cancellationToken)).Purchases;
    public async Task<Purchase?> GetPurchaseAsync(Guid id, CancellationToken cancellationToken = default) => (await LoadAsync(cancellationToken)).Purchases.FirstOrDefault(value => value.Id == id);
    public Task UpsertPurchaseAsync(Purchase purchase, CancellationToken cancellationToken = default) => MutateAsync(snapshot => Upsert(snapshot.Purchases, purchase, value => value.Id), cancellationToken);

    public async Task<IReadOnlyList<Customer>> GetCustomersAsync(CancellationToken cancellationToken = default) => (await LoadAsync(cancellationToken)).Customers;
    public Task UpsertCustomerAsync(Customer customer, CancellationToken cancellationToken = default) => MutateAsync(snapshot => Upsert(snapshot.Customers, customer, value => value.Id), cancellationToken);
    public Task DeleteCustomerAsync(Guid id, CancellationToken cancellationToken = default) => MutateAsync(snapshot => snapshot.Customers.RemoveAll(value => value.Id == id), cancellationToken);
    public async Task<IReadOnlyList<Sale>> GetSalesAsync(CancellationToken cancellationToken = default) => (await LoadAsync(cancellationToken)).Sales;
    public async Task<Sale?> GetSaleAsync(Guid id, CancellationToken cancellationToken = default) => (await LoadAsync(cancellationToken)).Sales.FirstOrDefault(value => value.Id == id);
    public Task UpsertSaleAsync(Sale sale, CancellationToken cancellationToken = default) => MutateAsync(snapshot => Upsert(snapshot.Sales, sale, value => value.Id), cancellationToken);

    public async Task<MarketingData> GetDataAsync(CancellationToken cancellationToken = default) => (await LoadAsync(cancellationToken)).Marketing;
    public async Task<IReadOnlyList<ProductCollection>> GetCollectionsAsync(CancellationToken cancellationToken = default) => (await LoadAsync(cancellationToken)).Marketing.Collections;
    public Task UpsertCollectionAsync(ProductCollection collection, CancellationToken cancellationToken = default) => MutateAsync(snapshot => Upsert(snapshot.Marketing.Collections, collection, value => value.Id), cancellationToken);
    public Task DeleteCollectionAsync(Guid id, CancellationToken cancellationToken = default) => MutateAsync(snapshot =>
    {
        snapshot.Marketing.Collections.RemoveAll(value => value.Id == id);
        foreach (var post in snapshot.Marketing.ContentPosts.Where(value => value.CollectionId == id)) { post.CollectionId = null; post.CollectionName = null; }
    }, cancellationToken);
    public async Task<IReadOnlyList<Outfit>> GetOutfitsAsync(CancellationToken cancellationToken = default) => (await LoadAsync(cancellationToken)).Marketing.Outfits;
    public Task UpsertOutfitAsync(Outfit outfit, CancellationToken cancellationToken = default) => MutateAsync(snapshot => Upsert(snapshot.Marketing.Outfits, outfit, value => value.Id), cancellationToken);
    public Task DeleteOutfitAsync(Guid id, CancellationToken cancellationToken = default) => MutateAsync(snapshot =>
    {
        snapshot.Marketing.Outfits.RemoveAll(value => value.Id == id);
        foreach (var post in snapshot.Marketing.ContentPosts.Where(value => value.OutfitId == id)) { post.OutfitId = null; post.OutfitName = null; }
    }, cancellationToken);
    public async Task<IReadOnlyList<ContentPost>> GetContentPostsAsync(CancellationToken cancellationToken = default) => (await LoadAsync(cancellationToken)).Marketing.ContentPosts;
    public Task UpsertContentPostAsync(ContentPost post, CancellationToken cancellationToken = default) => MutateAsync(snapshot => Upsert(snapshot.Marketing.ContentPosts, post, value => value.Id), cancellationToken);
    public Task DeleteContentPostAsync(Guid id, CancellationToken cancellationToken = default) => MutateAsync(snapshot => snapshot.Marketing.ContentPosts.RemoveAll(value => value.Id == id), cancellationToken);

    async Task<BusinessSettings> IBusinessSettingsRepository.GetAsync(CancellationToken cancellationToken) => (await LoadAsync(cancellationToken)).BusinessSettings;
    public Task SaveAsync(BusinessSettings settings, CancellationToken cancellationToken = default) => MutateAsync(snapshot => snapshot.BusinessSettings = settings, cancellationToken);

    async Task<IReadOnlyList<StockMovement>> IStockMovementRepository.GetAsync(CancellationToken cancellationToken) => (await LoadAsync(cancellationToken)).StockMovements;
    public Task AddRangeAsync(IEnumerable<StockMovement> movements, CancellationToken cancellationToken = default) => MutateAsync(snapshot =>
    {
        var ids = snapshot.StockMovements.Select(value => value.Id).ToHashSet();
        snapshot.StockMovements.AddRange(movements.Where(value => ids.Add(value.Id)));
    }, cancellationToken);

    async Task<PurchaseHistoryData> IPurchaseHistoryRepository.GetAsync(CancellationToken cancellationToken) => (await LoadAsync(cancellationToken)).PurchaseHistory;
    public Task AddAsync(IEnumerable<ProductCostHistoryEntry> productCosts, ExchangeRateHistoryEntry? exchangeRate, CancellationToken cancellationToken = default) => MutateAsync(snapshot =>
    {
        var ids = snapshot.PurchaseHistory.ProductCosts.Select(value => value.Id).ToHashSet();
        snapshot.PurchaseHistory.ProductCosts.AddRange(productCosts.Where(value => ids.Add(value.Id)));
        if (exchangeRate is not null && snapshot.PurchaseHistory.ExchangeRates.All(value => value.Id != exchangeRate.Id)) snapshot.PurchaseHistory.ExchangeRates.Add(exchangeRate);
    }, cancellationToken);

    public Task<DonaSyncSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default) => LoadAsync(cancellationToken);
    public Task ReplaceSnapshotAsync(DonaSyncSnapshot snapshot, CancellationToken cancellationToken = default) => SaveSnapshotAsync(snapshot, cancellationToken);

    private async Task<DonaSyncSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = await javascript.InvokeAsync<string?>("localStorage.getItem", cancellationToken, StorageKey);
        if (!string.IsNullOrWhiteSpace(json)) return JsonSerializer.Deserialize<DonaSyncSnapshot>(json, JsonOptions) ?? CreateInitialSnapshot();
        var initial = CreateInitialSnapshot();
        await SaveSnapshotAsync(initial, cancellationToken);
        return initial;
    }

    private async Task SaveSnapshotAsync(DonaSyncSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await javascript.InvokeVoidAsync("localStorage.setItem", cancellationToken, StorageKey, JsonSerializer.Serialize(snapshot, JsonOptions));
    }

    private async Task MutateAsync(Action<DonaSyncSnapshot> mutation, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await LoadAsync(cancellationToken);
            mutation(snapshot);
            await SaveSnapshotAsync(snapshot, cancellationToken);
        }
        finally { gate.Release(); }
    }

    private static void Upsert<T>(List<T> values, T value, Func<T, Guid> id)
    {
        var index = values.FindIndex(item => id(item) == id(value));
        if (index >= 0) values[index] = value; else values.Add(value);
    }

    private static DonaSyncSnapshot CreateInitialSnapshot() => new()
    {
        Categories = new[] { "Футболка", "Худи", "Рубашка", "Брюки", "Джинсы", "Куртка", "Сумка", "Аксессуары", "Другое" }
            .Select((name, index) => new Category { Name = name, SortOrder = index, IsActive = true })
            .ToList()
    };
}
