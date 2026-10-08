using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Storage.Sqlite;

public sealed class SqliteCatalogRepository(SqliteAggregateStore store) : ICatalogRepository
{
    private const string Products = "catalog.products";

    public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default) =>
        store.ReadCollectionAsync<Product>(Products, cancellationToken);

    public async Task<Product?> GetProductAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await GetProductsAsync(cancellationToken)).FirstOrDefault(product => product.Id == id);

    public Task UpsertProductAsync(Product product, CancellationToken cancellationToken = default) =>
        store.ApplyAsync([AggregateOperation.Upsert(Products, product.Id, product)], cancellationToken);

    public Task DeleteProductAsync(Guid id, CancellationToken cancellationToken = default) =>
        store.ApplyAsync([AggregateOperation.Delete(Products, id)], cancellationToken);
}

public sealed class SqliteCommerceRepository(SqliteAggregateStore store) : ICommerceRepository
{
    private const string Suppliers = "commerce.suppliers";
    private const string Intermediaries = "commerce.intermediaries";
    private const string Categories = "commerce.categories";
    private const string Purchases = "commerce.purchases";

    public Task<IReadOnlyList<Supplier>> GetSuppliersAsync(CancellationToken cancellationToken = default) => store.ReadCollectionAsync<Supplier>(Suppliers, cancellationToken);
    public Task<IReadOnlyList<Intermediary>> GetIntermediariesAsync(CancellationToken cancellationToken = default) => store.ReadCollectionAsync<Intermediary>(Intermediaries, cancellationToken);
    public Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default) => store.ReadCollectionAsync<Category>(Categories, cancellationToken);
    public Task<IReadOnlyList<Purchase>> GetPurchasesAsync(CancellationToken cancellationToken = default) => store.ReadCollectionAsync<Purchase>(Purchases, cancellationToken);
    public async Task<Purchase?> GetPurchaseAsync(Guid id, CancellationToken cancellationToken = default) => (await GetPurchasesAsync(cancellationToken)).FirstOrDefault(value => value.Id == id);
    public Task UpsertSupplierAsync(Supplier value, CancellationToken cancellationToken = default) => UpsertAsync(Suppliers, value.Id, value, cancellationToken);
    public Task DeleteSupplierAsync(Guid id, CancellationToken cancellationToken = default) => DeleteAsync(Suppliers, id, cancellationToken);
    public Task UpsertIntermediaryAsync(Intermediary value, CancellationToken cancellationToken = default) => UpsertAsync(Intermediaries, value.Id, value, cancellationToken);
    public Task DeleteIntermediaryAsync(Guid id, CancellationToken cancellationToken = default) => DeleteAsync(Intermediaries, id, cancellationToken);
    public Task UpsertCategoryAsync(Category value, CancellationToken cancellationToken = default) => UpsertAsync(Categories, value.Id, value, cancellationToken);
    public Task DeleteCategoryAsync(Guid id, CancellationToken cancellationToken = default) => DeleteAsync(Categories, id, cancellationToken);
    public Task UpsertPurchaseAsync(Purchase value, CancellationToken cancellationToken = default) => UpsertAsync(Purchases, value.Id, value, cancellationToken);

    private Task UpsertAsync<T>(string collection, Guid id, T value, CancellationToken cancellationToken) =>
        store.ApplyAsync([AggregateOperation.Upsert(collection, id, value)], cancellationToken);

    private Task DeleteAsync(string collection, Guid id, CancellationToken cancellationToken) =>
        store.ApplyAsync([AggregateOperation.Delete(collection, id)], cancellationToken);
}

public sealed class SqliteSalesRepository(SqliteAggregateStore store) : ISalesRepository
{
    private const string Customers = "sales.customers";
    private const string Sales = "sales.sales";

    public Task<IReadOnlyList<Customer>> GetCustomersAsync(CancellationToken cancellationToken = default) => store.ReadCollectionAsync<Customer>(Customers, cancellationToken);
    public Task<IReadOnlyList<Sale>> GetSalesAsync(CancellationToken cancellationToken = default) => store.ReadCollectionAsync<Sale>(Sales, cancellationToken);
    public async Task<Sale?> GetSaleAsync(Guid id, CancellationToken cancellationToken = default) => (await GetSalesAsync(cancellationToken)).FirstOrDefault(value => value.Id == id);
    public Task UpsertCustomerAsync(Customer value, CancellationToken cancellationToken = default) => store.ApplyAsync([AggregateOperation.Upsert(Customers, value.Id, value)], cancellationToken);
    public Task DeleteCustomerAsync(Guid id, CancellationToken cancellationToken = default) => store.ApplyAsync([AggregateOperation.Delete(Customers, id)], cancellationToken);
    public Task UpsertSaleAsync(Sale value, CancellationToken cancellationToken = default) => store.ApplyAsync([AggregateOperation.Upsert(Sales, value.Id, value)], cancellationToken);
}

public sealed class SqliteInventoryStore(SqliteAggregateStore store) : IInventoryStore
{
    private const string Products = "catalog.products";
    private const string Sales = "sales.sales";
    private const string Purchases = "commerce.purchases";
    private const string Movements = "stock.movements";
    private const string ProductCosts = "history.product-costs";
    private const string ExchangeRates = "history.exchange-rates";

    public Task CommitAsync(InventoryCommit commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        commit.ValidateLayers();
        var operations = new List<AggregateOperation>();
        operations.AddRange(commit.Products.Select(value => AggregateOperation.Upsert(Products, value.Id, value)));
        operations.AddRange(commit.Sales.Select(value => AggregateOperation.Upsert(Sales, value.Id, value)));
        operations.AddRange(commit.Purchases.Select(value => AggregateOperation.Upsert(Purchases, value.Id, value)));
        operations.AddRange(commit.Movements.Select(value => AggregateOperation.InsertIfMissing(Movements, value.Id, value)));
        operations.AddRange(commit.ProductCosts.Select(value => AggregateOperation.InsertIfMissing(ProductCosts, value.Id, value)));
        operations.AddRange(commit.CostCorrections.Select(value => AggregateOperation.Upsert(ProductCosts, value.Id, value)));
        if (commit.ExchangeRate is { } rate) operations.Add(AggregateOperation.InsertIfMissing(ExchangeRates, rate.Id, rate));
        return store.ApplyAsync(operations, cancellationToken);
    }
}
