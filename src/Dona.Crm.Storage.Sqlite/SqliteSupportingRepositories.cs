using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Storage.Sqlite;

public sealed class SqliteMarketingRepository(SqliteAggregateStore store) : IMarketingRepository
{
    private const string Collections = "marketing.collections";
    private const string Outfits = "marketing.outfits";
    private const string Posts = "marketing.posts";
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<MarketingData> GetDataAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return new MarketingData
            {
                Collections = (await store.ReadCollectionAsync<ProductCollection>(Collections, cancellationToken)).ToList(),
                Outfits = (await store.ReadCollectionAsync<Outfit>(Outfits, cancellationToken)).ToList(),
                ContentPosts = (await store.ReadCollectionAsync<ContentPost>(Posts, cancellationToken)).ToList()
            };
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<ProductCollection>> GetCollectionsAsync(CancellationToken cancellationToken = default) =>
        (await GetDataAsync(cancellationToken)).Collections;

    public async Task<IReadOnlyList<Outfit>> GetOutfitsAsync(CancellationToken cancellationToken = default) =>
        (await GetDataAsync(cancellationToken)).Outfits;

    public async Task<IReadOnlyList<ContentPost>> GetContentPostsAsync(CancellationToken cancellationToken = default) =>
        (await GetDataAsync(cancellationToken)).ContentPosts;

    public Task UpsertCollectionAsync(ProductCollection value, CancellationToken cancellationToken = default) =>
        MutateAsync(data => Upsert(data.Collections, value, item => item.Id), cancellationToken);

    public Task DeleteCollectionAsync(Guid id, CancellationToken cancellationToken = default) =>
        MutateAsync(data =>
        {
            data.Collections.RemoveAll(item => item.Id == id);
            foreach (var post in data.ContentPosts.Where(item => item.CollectionId == id))
            {
                post.CollectionId = null;
                post.CollectionName = null;
            }
        }, cancellationToken);

    public Task UpsertOutfitAsync(Outfit value, CancellationToken cancellationToken = default) =>
        MutateAsync(data => Upsert(data.Outfits, value, item => item.Id), cancellationToken);

    public Task DeleteOutfitAsync(Guid id, CancellationToken cancellationToken = default) =>
        MutateAsync(data =>
        {
            data.Outfits.RemoveAll(item => item.Id == id);
            foreach (var post in data.ContentPosts.Where(item => item.OutfitId == id))
            {
                post.OutfitId = null;
                post.OutfitName = null;
            }
        }, cancellationToken);

    public Task UpsertContentPostAsync(ContentPost value, CancellationToken cancellationToken = default) =>
        MutateAsync(data => Upsert(data.ContentPosts, value, item => item.Id), cancellationToken);

    public Task DeleteContentPostAsync(Guid id, CancellationToken cancellationToken = default) =>
        MutateAsync(data => data.ContentPosts.RemoveAll(item => item.Id == id), cancellationToken);

    private async Task MutateAsync(Action<MarketingData> mutation, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var data = new MarketingData
            {
                Collections = (await store.ReadCollectionAsync<ProductCollection>(Collections, cancellationToken)).ToList(),
                Outfits = (await store.ReadCollectionAsync<Outfit>(Outfits, cancellationToken)).ToList(),
                ContentPosts = (await store.ReadCollectionAsync<ContentPost>(Posts, cancellationToken)).ToList()
            };

            mutation(data);
            await store.ReplaceCollectionAsync(Collections, data.Collections.Select(item => (item.Id, item)), cancellationToken);
            await store.ReplaceCollectionAsync(Outfits, data.Outfits.Select(item => (item.Id, item)), cancellationToken);
            await store.ReplaceCollectionAsync(Posts, data.ContentPosts.Select(item => (item.Id, item)), cancellationToken);
        }
        finally { gate.Release(); }
    }

    private static void Upsert<T>(List<T> values, T value, Func<T, Guid> id)
    {
        var index = values.FindIndex(item => id(item) == id(value));
        if (index >= 0) values[index] = value; else values.Add(value);
    }
}

public sealed class SqliteBusinessSettingsRepository(SqliteAggregateStore store) : IBusinessSettingsRepository
{
    private const string Settings = "settings.business";
    private static readonly Guid SettingsId = Guid.Parse("59b746f0-32f8-48a3-b1eb-ecf57969093a");

    public async Task<BusinessSettings> GetAsync(CancellationToken cancellationToken = default) =>
        (await store.ReadCollectionAsync<BusinessSettings>(Settings, cancellationToken)).FirstOrDefault() ?? new BusinessSettings();

    public Task SaveAsync(BusinessSettings settings, CancellationToken cancellationToken = default) =>
        store.ReplaceCollectionAsync(Settings, [(SettingsId, settings)], cancellationToken);
}

public sealed class SqliteStockMovementRepository(SqliteAggregateStore store) : IStockMovementRepository
{
    private const string Movements = "stock.movements";
    private readonly SemaphoreSlim gate = new(1, 1);

    public Task<IReadOnlyList<StockMovement>> GetAsync(CancellationToken cancellationToken = default) =>
        store.ReadCollectionAsync<StockMovement>(Movements, cancellationToken);

    public async Task AddRangeAsync(IEnumerable<StockMovement> movements, CancellationToken cancellationToken = default)
    {
        var additions = movements.ToList();
        if (additions.Count == 0) return;

        await gate.WaitAsync(cancellationToken);
        try
        {
            var values = (await store.ReadCollectionAsync<StockMovement>(Movements, cancellationToken)).ToList();
            values.AddRange(additions);
            await store.ReplaceCollectionAsync(Movements, values.Select(item => (item.Id, item)), cancellationToken);
        }
        finally { gate.Release(); }
    }
}

public sealed class SqlitePurchaseHistoryRepository(SqliteAggregateStore store) : IPurchaseHistoryRepository
{
    private const string ProductCosts = "history.product-costs";
    private const string ExchangeRates = "history.exchange-rates";
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<PurchaseHistoryData> GetAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return new PurchaseHistoryData
            {
                ProductCosts = (await store.ReadCollectionAsync<ProductCostHistoryEntry>(ProductCosts, cancellationToken)).ToList(),
                ExchangeRates = (await store.ReadCollectionAsync<ExchangeRateHistoryEntry>(ExchangeRates, cancellationToken)).ToList()
            };
        }
        finally { gate.Release(); }
    }

    public async Task AddAsync(
        IEnumerable<ProductCostHistoryEntry> productCosts,
        ExchangeRateHistoryEntry? exchangeRate,
        CancellationToken cancellationToken = default)
    {
        var additions = productCosts.ToList();
        await gate.WaitAsync(cancellationToken);
        try
        {
            var costs = (await store.ReadCollectionAsync<ProductCostHistoryEntry>(ProductCosts, cancellationToken)).ToList();
            var costIds = costs.Select(item => item.Id).ToHashSet();
            costs.AddRange(additions.Where(item => costIds.Add(item.Id)));

            var rates = (await store.ReadCollectionAsync<ExchangeRateHistoryEntry>(ExchangeRates, cancellationToken)).ToList();
            if (exchangeRate is not null && rates.All(item => item.Id != exchangeRate.Id))
                rates.Add(exchangeRate);

            await store.ReplaceCollectionAsync(ProductCosts, costs.Select(item => (item.Id, item)), cancellationToken);
            await store.ReplaceCollectionAsync(ExchangeRates, rates.Select(item => (item.Id, item)), cancellationToken);
        }
        finally { gate.Release(); }
    }
}
