using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Storage.Sqlite;

public sealed class SqliteCatalogRepository(SqliteAggregateStore store) : ICatalogRepository
{
    private const string Products = "catalog.products";
    private const string SeedMarker = "metadata.catalog.v1";
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);
        return await store.ReadCollectionAsync<Product>(Products, cancellationToken);
    }

    public async Task<Product?> GetProductAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await GetProductsAsync(cancellationToken)).FirstOrDefault(product => product.Id == id);

    public Task UpsertProductAsync(Product product, CancellationToken cancellationToken = default) =>
        MutateAsync(values => Upsert(values, product, value => value.Id), cancellationToken);

    public Task DeleteProductAsync(Guid id, CancellationToken cancellationToken = default) =>
        MutateAsync(values => values.RemoveAll(value => value.Id == id), cancellationToken);

    private async Task EnsureSeededAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if ((await store.ReadCollectionAsync<StorageMarker>(SeedMarker, cancellationToken)).Count > 0)
                return;

            var products = await store.ReadCollectionAsync<Product>(Products, cancellationToken);
            if (products.Count == 0)
                await store.ReplaceCollectionAsync(Products, SqliteSeedData.Products.Select(value => (value.Id, value)), cancellationToken);

            var marker = new StorageMarker(Guid.NewGuid(), 1);
            await store.ReplaceCollectionAsync(SeedMarker, [(marker.Id, marker)], cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task MutateAsync(Action<List<Product>> mutation, CancellationToken cancellationToken)
    {
        await EnsureSeededAsync(cancellationToken);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var values = (await store.ReadCollectionAsync<Product>(Products, cancellationToken)).ToList();
            mutation(values);
            await store.ReplaceCollectionAsync(Products, values.Select(value => (value.Id, value)), cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private static void Upsert<T>(List<T> values, T value, Func<T, Guid> id)
    {
        var index = values.FindIndex(item => id(item) == id(value));
        if (index >= 0) values[index] = value; else values.Add(value);
    }
}

public sealed class SqliteCommerceRepository(SqliteAggregateStore store) : ICommerceRepository
{
    private const string Suppliers = "commerce.suppliers";
    private const string Intermediaries = "commerce.intermediaries";
    private const string Categories = "commerce.categories";
    private const string Purchases = "commerce.purchases";
    private const string SeedMarker = "metadata.commerce.v1";
    private readonly SemaphoreSlim gate = new(1, 1);

    public Task<IReadOnlyList<Supplier>> GetSuppliersAsync(CancellationToken cancellationToken = default) => store.ReadCollectionAsync<Supplier>(Suppliers, cancellationToken);
    public Task<IReadOnlyList<Intermediary>> GetIntermediariesAsync(CancellationToken cancellationToken = default) => store.ReadCollectionAsync<Intermediary>(Intermediaries, cancellationToken);
    public async Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default) { await EnsureSeededAsync(cancellationToken); return await store.ReadCollectionAsync<Category>(Categories, cancellationToken); }
    public Task<IReadOnlyList<Purchase>> GetPurchasesAsync(CancellationToken cancellationToken = default) => store.ReadCollectionAsync<Purchase>(Purchases, cancellationToken);
    public async Task<Purchase?> GetPurchaseAsync(Guid id, CancellationToken cancellationToken = default) => (await GetPurchasesAsync(cancellationToken)).FirstOrDefault(value => value.Id == id);
    public Task UpsertSupplierAsync(Supplier value, CancellationToken cancellationToken = default) => MutateAsync(Suppliers, value, item => item.Id, cancellationToken);
    public Task DeleteSupplierAsync(Guid id, CancellationToken cancellationToken = default) => DeleteAsync<Supplier>(Suppliers, id, item => item.Id, cancellationToken);
    public Task UpsertIntermediaryAsync(Intermediary value, CancellationToken cancellationToken = default) => MutateAsync(Intermediaries, value, item => item.Id, cancellationToken);
    public Task DeleteIntermediaryAsync(Guid id, CancellationToken cancellationToken = default) => DeleteAsync<Intermediary>(Intermediaries, id, item => item.Id, cancellationToken);
    public async Task UpsertCategoryAsync(Category value, CancellationToken cancellationToken = default) { await EnsureSeededAsync(cancellationToken); await MutateAsync(Categories, value, item => item.Id, cancellationToken); }
    public async Task DeleteCategoryAsync(Guid id, CancellationToken cancellationToken = default) { await EnsureSeededAsync(cancellationToken); await DeleteAsync<Category>(Categories, id, item => item.Id, cancellationToken); }
    public Task UpsertPurchaseAsync(Purchase value, CancellationToken cancellationToken = default) => MutateAsync(Purchases, value, item => item.Id, cancellationToken);

    private async Task EnsureSeededAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if ((await store.ReadCollectionAsync<StorageMarker>(SeedMarker, cancellationToken)).Count > 0)
                return;

            var categories = await store.ReadCollectionAsync<Category>(Categories, cancellationToken);
            if (categories.Count == 0)
                await store.ReplaceCollectionAsync(Categories, SqliteSeedData.Categories.Select(value => (value.Id, value)), cancellationToken);

            var marker = new StorageMarker(Guid.NewGuid(), 1);
            await store.ReplaceCollectionAsync(SeedMarker, [(marker.Id, marker)], cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task MutateAsync<T>(string collection, T value, Func<T, Guid> id, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var values = (await store.ReadCollectionAsync<T>(collection, cancellationToken)).ToList();
            var index = values.FindIndex(item => id(item) == id(value));
            if (index >= 0) values[index] = value; else values.Add(value);
            await store.ReplaceCollectionAsync(collection, values.Select(item => (id(item), item)), cancellationToken);
        }
        finally { gate.Release(); }
    }

    private async Task DeleteAsync<T>(string collection, Guid id, Func<T, Guid> getId, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var values = (await store.ReadCollectionAsync<T>(collection, cancellationToken)).ToList();
            values.RemoveAll(item => getId(item) == id);
            await store.ReplaceCollectionAsync(collection, values.Select(item => (getId(item), item)), cancellationToken);
        }
        finally { gate.Release(); }
    }
}

public sealed class SqliteSalesRepository(SqliteAggregateStore store) : ISalesRepository
{
    private const string Customers = "sales.customers";
    private const string Sales = "sales.sales";
    private readonly SemaphoreSlim gate = new(1, 1);

    public Task<IReadOnlyList<Customer>> GetCustomersAsync(CancellationToken cancellationToken = default) => store.ReadCollectionAsync<Customer>(Customers, cancellationToken);
    public Task<IReadOnlyList<Sale>> GetSalesAsync(CancellationToken cancellationToken = default) => store.ReadCollectionAsync<Sale>(Sales, cancellationToken);
    public async Task<Sale?> GetSaleAsync(Guid id, CancellationToken cancellationToken = default) => (await GetSalesAsync(cancellationToken)).FirstOrDefault(value => value.Id == id);
    public Task UpsertCustomerAsync(Customer value, CancellationToken cancellationToken = default) => MutateAsync(Customers, value, item => item.Id, cancellationToken);
    public Task DeleteCustomerAsync(Guid id, CancellationToken cancellationToken = default) => DeleteAsync<Customer>(Customers, id, item => item.Id, cancellationToken);
    public Task UpsertSaleAsync(Sale value, CancellationToken cancellationToken = default) => MutateAsync(Sales, value, item => item.Id, cancellationToken);

    private async Task MutateAsync<T>(string collection, T value, Func<T, Guid> id, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var values = (await store.ReadCollectionAsync<T>(collection, cancellationToken)).ToList();
            var index = values.FindIndex(item => id(item) == id(value));
            if (index >= 0) values[index] = value; else values.Add(value);
            await store.ReplaceCollectionAsync(collection, values.Select(item => (id(item), item)), cancellationToken);
        }
        finally { gate.Release(); }
    }

    private async Task DeleteAsync<T>(string collection, Guid id, Func<T, Guid> getId, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var values = (await store.ReadCollectionAsync<T>(collection, cancellationToken)).ToList();
            values.RemoveAll(item => getId(item) == id);
            await store.ReplaceCollectionAsync(collection, values.Select(item => (getId(item), item)), cancellationToken);
        }
        finally { gate.Release(); }
    }
}

internal sealed record StorageMarker(Guid Id, int Version);

internal static class SqliteSeedData
{
    public static IReadOnlyList<Product> Products { get; } =
    [
        new() { Sku = "TS-0001", Name = "Oversize футболка Basic", Category = "Футболка", Status = ProductStatus.InStock, PurchasePriceCny = 28, DeliveryCostUzs = 18_000, SellingPriceUzs = 119_000, Variants = [new() { Color = "Чёрный", Size = "M", Quantity = 5 }, new() { Color = "Чёрный", Size = "L", Quantity = 3 }] },
        new() { Sku = "BG-0001", Name = "Сумка City Mini", Category = "Сумка", Status = ProductStatus.OnOrder, PurchasePriceCny = 42, DeliveryCostUzs = 24_000, SellingPriceUzs = 169_000 },
        new() { Sku = "HD-0001", Name = "Худи Minimal", Category = "Худи", Status = ProductStatus.LowStock, PurchasePriceCny = 75, DeliveryCostUzs = 36_000, SellingPriceUzs = 279_000, Variants = [new() { Color = "Бежевый", Size = "L", Quantity = 2 }] }
    ];

    public static IReadOnlyList<Category> Categories { get; } =
        new[] { "Футболка", "Худи", "Рубашка", "Брюки", "Джинсы", "Куртка", "Сумка", "Кепка", "Ремень", "Украшения", "Другое" }
            .Select((name, index) => new Category { Name = name, SortOrder = index, IsActive = true })
            .ToList();
}
