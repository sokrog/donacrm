using System.Text.Json;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Core.Tests;

internal sealed class MemoryInventoryStore(CloningCatalog? catalog = null) : IInventoryStore
{
    public List<InventoryCommit> Commits { get; } = [];
    public List<StockMovement> Movements { get; } = [];
    public PurchaseHistoryData History { get; } = new();
    public Exception? FailWith { get; set; }

    public Task CommitAsync(InventoryCommit commit, CancellationToken cancellationToken = default)
    {
        if (FailWith is not null) return Task.FromException(FailWith);
        Commits.Add(commit);
        var movementIds = Movements.Select(x => x.Id).ToHashSet();
        Movements.AddRange(commit.Movements.Where(x => movementIds.Add(x.Id)));
        var costIds = History.ProductCosts.Select(x => x.Id).ToHashSet();
        History.ProductCosts.AddRange(commit.ProductCosts.Where(x => costIds.Add(x.Id)));
        if (commit.ExchangeRate is { } rate && History.ExchangeRates.All(x => x.Id != rate.Id)) History.ExchangeRates.Add(rate);
        if (catalog is not null) foreach (var product in commit.Products) catalog.Put(product);
        return Task.CompletedTask;
    }
}

/// <summary>Каталог, который отдаёт копии, как настоящие репозитории: правки видны только после коммита.</summary>
internal sealed class CloningCatalog : ICatalogRepository
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<Guid, string> items = [];

    public CloningCatalog(params Product[] products) { foreach (var product in products) Put(FifoFixtures.OpeningBalance(product)); }
    public void Put(Product product) => items[product.Id] = JsonSerializer.Serialize(product, Options);
    public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Product>>(items.Values.Select(x => JsonSerializer.Deserialize<Product>(x, Options)!).ToList());
    public Task<Product?> GetProductAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(items.TryGetValue(id, out var json) ? JsonSerializer.Deserialize<Product>(json, Options) : null);
    public Task UpsertProductAsync(Product product, CancellationToken cancellationToken = default) { Put(product); return Task.CompletedTask; }
    public Task DeleteProductAsync(Guid id, CancellationToken cancellationToken = default) { items.Remove(id); return Task.CompletedTask; }
}

public sealed class InventoryAtomicityTests
{
    private static (Product Product, ProductVariant Variant, Sale Sale, SaleItem Item) CreateSale(int stock = 10, int quantity = 3)
    {
        var variant = new ProductVariant { Color = "Чёрный", Size = "M", Quantity = stock };
        var product = new Product { Name = "Футболка", Sku = "TS-ATOM", Variants = [variant] };
        var item = new SaleItem { ProductId = product.Id, ProductVariantId = variant.Id, ProductName = product.Name, Color = variant.Color, Size = variant.Size, Quantity = quantity, UnitPriceUzs = 100 };
        return (product, variant, new Sale { Number = "SALE-A", Items = [item] }, item);
    }

    [Fact]
    public async Task Successful_reserve_commits_product_sale_and_movement_once()
    {
        var (product, _, sale, _) = CreateSale();
        var store = new MemoryInventoryStore();

        await new SalesInventoryService(new CloningCatalog(product), store).ReserveAsync(sale);

        var commit = Assert.Single(store.Commits);
        Assert.Single(commit.Products);
        Assert.Single(commit.Sales);
        Assert.Equal(SaleStatus.Reserved, commit.Sales[0].Status);
        var movement = Assert.Single(commit.Movements);
        Assert.Equal(StockMovementType.Reservation, movement.Type);
        Assert.Equal(3, movement.ReservedDelta);
    }

    [Fact]
    public async Task Failed_reserve_leaves_sale_and_stock_untouched_and_retry_still_reserves()
    {
        var (product, variant, sale, item) = CreateSale();
        var catalog = new CloningCatalog(product);
        var store = new MemoryInventoryStore(catalog) { FailWith = new IOException("диск недоступен") };
        var service = new SalesInventoryService(catalog, store);

        await Assert.ThrowsAsync<IOException>(() => service.ReserveAsync(sale));

        Assert.Null(sale.Status);
        Assert.Equal(0, item.ReservedQuantity);
        Assert.Same(item, sale.Items[0]);
        Assert.Empty(store.Commits);
        Assert.Equal(0, (await catalog.GetProductAsync(product.Id))!.Variants[0].ReservedQuantity);

        store.FailWith = null;
        await service.ReserveAsync(sale);

        Assert.Equal(SaleStatus.Reserved, sale.Status);
        Assert.Equal(3, item.ReservedQuantity);
        Assert.Equal(3, (await catalog.GetProductAsync(product.Id))!.Variants[0].ReservedQuantity);
        Assert.Equal(10, variant.Quantity);
    }

    [Fact]
    public async Task Failed_complete_keeps_reservation_and_can_be_repeated()
    {
        var (product, _, sale, item) = CreateSale();
        var catalog = new CloningCatalog(product);
        var store = new MemoryInventoryStore(catalog);
        var service = new SalesInventoryService(catalog, store);
        await service.ReserveAsync(sale);

        store.FailWith = new IOException("сбой");
        await Assert.ThrowsAsync<IOException>(() => service.CompleteAsync(sale));

        Assert.Equal(SaleStatus.Reserved, sale.Status);
        Assert.Equal(0, item.SoldQuantity);
        Assert.Equal(3, item.ReservedQuantity);
        var stored = (await catalog.GetProductAsync(product.Id))!.Variants[0];
        Assert.Equal(10, stored.Quantity);
        Assert.Equal(3, stored.ReservedQuantity);

        store.FailWith = null;
        await service.CompleteAsync(sale);

        Assert.Equal(SaleStatus.Completed, sale.Status);
        stored = (await catalog.GetProductAsync(product.Id))!.Variants[0];
        Assert.Equal(7, stored.Quantity);
        Assert.Equal(0, stored.ReservedQuantity);
    }

    [Fact]
    public async Task Failed_payment_removes_the_payment_from_the_callers_sale()
    {
        var sale = new Sale { Number = "SALE-P", Status = SaleStatus.Reserved, Items = [new SaleItem { ProductName = "Худи", Quantity = 1, UnitPriceUzs = 100 }] };
        var store = new MemoryInventoryStore { FailWith = new IOException("сбой") };
        var payment = new SalePayment { Type = PaymentOperationType.Payment, Status = PaymentStatus.Completed, Method = PaymentMethod.Cash, AmountUzs = 100 };

        await Assert.ThrowsAsync<IOException>(() => new SalesPaymentService(store).AddAsync(sale, payment));

        Assert.Empty(sale.Payments);
        Assert.Null(sale.PaymentMethod);
        Assert.Equal(SaleStatus.Reserved, sale.Status);
    }

    [Fact]
    public async Task Failed_return_does_not_change_sale_or_stock()
    {
        var variant = new ProductVariant { Quantity = 7 };
        var product = new Product { Name = "Футболка", Sku = "TS-RET", Variants = [variant] };
        var item = new SaleItem { ProductId = product.Id, ProductVariantId = variant.Id, ProductName = product.Name, Quantity = 3, SoldQuantity = 3, UnitPriceUzs = 100 };
        FifoFixtures.CompletedSale(variant, item);
        var sale = new Sale { Number = "SALE-RR", Status = SaleStatus.Completed, Items = [item] };
        var catalog = new CloningCatalog(product);
        var store = new MemoryInventoryStore(catalog) { FailWith = new IOException("сбой") };
        var document = new SaleReturn { Reason = "Размер", RefundAmountUzs = 100, Items = [new SaleReturnItem { SaleItemId = item.Id, Quantity = 1, Disposition = ReturnDisposition.Restock }] };

        await Assert.ThrowsAsync<IOException>(() => new SalesReturnService(catalog, store).CreateAsync(sale, document));

        Assert.Empty(sale.Returns);
        Assert.Equal(0, item.ReturnedQuantity);
        Assert.Equal(7, (await catalog.GetProductAsync(product.Id))!.Variants[0].Quantity);

        store.FailWith = null;
        await new SalesReturnService(catalog, store).CreateAsync(sale, document);
        var commit = Assert.Single(store.Commits);
        Assert.Single(commit.Products);
        Assert.Single(commit.Movements);
        Assert.Equal(8, (await catalog.GetProductAsync(product.Id))!.Variants[0].Quantity);
    }

    [Fact]
    public async Task Purchase_receipt_is_one_commit_with_stock_movement_and_history()
    {
        var variant = new ProductVariant { Color = "Чёрный", Size = "M", Quantity = 2 };
        var product = new Product { Name = "Футболка", Sku = "TS-PO", Variants = [variant] };
        var item = new PurchaseItem { ProductId = product.Id, ProductVariantId = variant.Id, ProductName = product.Name, Color = variant.Color, Size = variant.Size, Quantity = 10, UnitPriceCny = 20 };
        var purchase = new Purchase { Number = "PO-A", SupplierName = "Store", CnyRateUzs = 1_800, Items = [item] };
        var store = new MemoryInventoryStore();

        var result = await new PurchaseReceivingService(new CloningCatalog(product), store).ReceiveAsync(purchase, [new PurchaseReceiptInput(item.Id, 8, 1)]);

        Assert.Equal(7, result.AddedUnits);
        var commit = Assert.Single(store.Commits);
        Assert.Single(commit.Products);
        Assert.Equal(9, commit.Products[0].Variants[0].Quantity);
        Assert.Single(commit.Purchases);
        Assert.Single(commit.Movements);
        Assert.Single(commit.ProductCosts);
        Assert.NotNull(commit.ExchangeRate);
    }

    [Fact]
    public async Task Failed_purchase_receipt_restores_the_purchase()
    {
        var variant = new ProductVariant { Color = "Чёрный", Size = "M", Quantity = 2 };
        var product = new Product { Name = "Футболка", Sku = "TS-PO2", Variants = [variant] };
        var item = new PurchaseItem { ProductId = product.Id, ProductVariantId = variant.Id, ProductName = product.Name, Color = variant.Color, Size = variant.Size, Quantity = 10, UnitPriceCny = 20 };
        var purchase = new Purchase { Number = "PO-B", CnyRateUzs = 1_800, Items = [item] };
        var catalog = new CloningCatalog(product);
        var store = new MemoryInventoryStore(catalog) { FailWith = new IOException("сбой") };
        var service = new PurchaseReceivingService(catalog, store);
        var input = new[] { new PurchaseReceiptInput(item.Id, 8, 1) };

        await Assert.ThrowsAsync<IOException>(() => service.ReceiveAsync(purchase, input));

        Assert.Empty(purchase.Receipts);
        Assert.Null(purchase.Status);
        Assert.Null(item.ReceivedQuantity);
        Assert.Equal(0, item.StockedQuantity);
        Assert.Equal(2, (await catalog.GetProductAsync(product.Id))!.Variants[0].Quantity);

        store.FailWith = null;
        var result = await service.ReceiveAsync(purchase, input);
        Assert.Equal(7, result.AddedUnits);
        Assert.Equal(9, (await catalog.GetProductAsync(product.Id))!.Variants[0].Quantity);
    }

    [Fact]
    public async Task Adjustment_commits_product_and_movement_together()
    {
        var variant = new ProductVariant { Quantity = 10, ReservedQuantity = 3 };
        var product = new Product { Name = "Худи", Sku = "HD-ADJ", Variants = [variant] };
        var catalog = new CloningCatalog(product);
        var store = new MemoryInventoryStore(catalog);

        await new StockAdjustmentService(catalog, store).AdjustAsync(new StockAdjustmentRequest { ProductId = product.Id, ProductVariantId = variant.Id, Reason = StockAdjustmentReason.InventoryCount, NewQuantity = 7, Note = "Пересчёт" });

        var commit = Assert.Single(store.Commits);
        Assert.Single(commit.Products);
        Assert.Equal(-3, Assert.Single(commit.Movements).QuantityDelta);
    }
}

public sealed class ProductEditingServiceTests
{
    private sealed class StubSales(params Sale[] sales) : ISalesRepository
    {
        public Task<IReadOnlyList<Customer>> GetCustomersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Customer>>([]);
        public Task UpsertCustomerAsync(Customer customer, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteCustomerAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Sale>> GetSalesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Sale>>(sales);
        public Task<Sale?> GetSaleAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(sales.FirstOrDefault(x => x.Id == id));
        public Task UpsertSaleAsync(Sale sale, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubCommerce(params Purchase[] purchases) : ICommerceRepository
    {
        public Task<IReadOnlyList<Supplier>> GetSuppliersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Supplier>>([]);
        public Task UpsertSupplierAsync(Supplier supplier, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteSupplierAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Intermediary>> GetIntermediariesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Intermediary>>([]);
        public Task UpsertIntermediaryAsync(Intermediary intermediary, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteIntermediaryAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Category>>([]);
        public Task UpsertCategoryAsync(Category category, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteCategoryAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Purchase>> GetPurchasesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Purchase>>(purchases);
        public Task<Purchase?> GetPurchaseAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(purchases.FirstOrDefault(x => x.Id == id));
        public Task UpsertPurchaseAsync(Purchase purchase, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubSettings : IBusinessSettingsRepository
    {
        public Task<BusinessSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(new BusinessSettings());
        public Task SaveAsync(BusinessSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static Product Copy(Product product) => JsonSerializer.Deserialize<Product>(JsonSerializer.Serialize(product, Json), Json)!;

    private static (ProductEditingService Service, CloningCatalog Catalog, MemoryInventoryStore Store) Create(Product? stored, Sale[]? sales = null, Purchase[]? purchases = null)
    {
        var catalog = stored is null ? new CloningCatalog() : new CloningCatalog(stored);
        var store = new MemoryInventoryStore(catalog);
        return (new ProductEditingService(catalog, new StubSales(sales ?? []), new StubCommerce(purchases ?? []), new StubSettings(), new ProductStatusService(), store), catalog, store);
    }

    [Fact]
    public async Task Stale_editor_copy_does_not_overwrite_stock_or_reservation()
    {
        var variant = new ProductVariant { Color = "Чёрный", Size = "M", Quantity = 10, ReservedQuantity = 3 };
        var stored = new Product { Name = "Футболка", Sku = "TS-1", Variants = [variant] };
        var (service, catalog, store) = Create(stored);
        var edited = Copy(stored);
        edited.Variants[0].Quantity = 99;
        edited.Variants[0].ReservedQuantity = 0;
        edited.Name = "Футболка Oversize";
        edited.Variants[0].Size = "L";
        edited.SellingPriceUzs = 150_000;

        await service.SaveAsync(edited);

        var actual = (await catalog.GetProductAsync(stored.Id))!;
        Assert.Equal("Футболка Oversize", actual.Name);
        Assert.Equal(150_000, actual.SellingPriceUzs);
        Assert.Equal("L", actual.Variants[0].Size);
        Assert.Equal(10, actual.Variants[0].Quantity);
        Assert.Equal(3, actual.Variants[0].ReservedQuantity);
        Assert.Empty(store.Movements);
    }

    [Fact]
    public async Task New_variants_get_initial_stock_and_a_journal_entry()
    {
        var stored = new Product { Name = "Футболка", Sku = "TS-2", Variants = [new ProductVariant { Color = "Чёрный", Size = "M", Quantity = 4 }] };
        var (service, catalog, store) = Create(stored);
        var edited = Copy(stored);
        edited.Variants.Add(new ProductVariant { Color = "Белый", Size = "M", Quantity = 5 });
        edited.Variants.Add(new ProductVariant { Color = "Белый", Size = "L", Quantity = 0 });

        await service.SaveAsync(edited);

        var actual = (await catalog.GetProductAsync(stored.Id))!;
        Assert.Equal(3, actual.Variants.Count);
        Assert.Equal(9, actual.Quantity);
        var movement = Assert.Single(store.Movements);
        Assert.Equal(StockMovementType.Adjustment, movement.Type);
        Assert.Equal(5, movement.QuantityDelta);
        Assert.Equal("Product", movement.SourceType);
        Assert.Equal("Начальный остаток", movement.Note);
        Assert.Equal(actual.Variants[1].Id, movement.ProductVariantId);
        Assert.Single(store.Commits);
    }

    [Fact]
    public async Task New_product_is_saved_with_initial_stock_movement()
    {
        var (service, catalog, store) = Create(null);
        var product = new Product { Name = "Сумка", Sku = "BG-1", Variants = [new ProductVariant { Color = "Бежевый", Size = "U", Quantity = 6 }] };

        await service.SaveAsync(product);

        Assert.Equal(6, (await catalog.GetProductAsync(product.Id))!.Quantity);
        Assert.Equal(6, Assert.Single(store.Movements).QuantityDelta);
    }

    [Fact]
    public async Task Refuses_to_remove_variant_with_stock_reserve_or_references()
    {
        var stocked = new ProductVariant { Color = "A", Size = "M", Quantity = 2 };
        var reserved = new ProductVariant { Color = "B", Size = "M", Quantity = 0, ReservedQuantity = 1 };
        var inSale = new ProductVariant { Color = "C", Size = "M", Quantity = 0 };
        var inPurchase = new ProductVariant { Color = "D", Size = "M", Quantity = 0 };
        var stored = new Product { Name = "Футболка", Sku = "TS-3", Variants = [stocked, reserved, inSale, inPurchase] };
        var sale = new Sale { Number = "S", Items = [new SaleItem { ProductId = stored.Id, ProductVariantId = inSale.Id }] };
        var purchase = new Purchase { Number = "P", Items = [new PurchaseItem { ProductId = stored.Id, ProductVariantId = inPurchase.Id, ProductName = "Футболка" }] };
        var (service, _, store) = Create(stored, [sale], [purchase]);

        foreach (var variant in new[] { stocked, reserved, inSale, inPurchase })
        {
            var edited = Copy(stored);
            edited.Variants.RemoveAll(x => x.Id == variant.Id);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(edited));
            Assert.Contains("Корректировка остатка", error.Message);
        }
        Assert.Empty(store.Commits);
    }

    [Fact]
    public async Task Removes_empty_unreferenced_variant()
    {
        var keep = new ProductVariant { Color = "A", Size = "M", Quantity = 2 };
        var empty = new ProductVariant { Color = "B", Size = "M", Quantity = 0 };
        var stored = new Product { Name = "Футболка", Sku = "TS-4", Variants = [keep, empty] };
        var (service, catalog, _) = Create(stored);
        var edited = Copy(stored);
        edited.Variants.RemoveAll(x => x.Id == empty.Id);

        await service.SaveAsync(edited);

        Assert.Equal(keep.Id, Assert.Single((await catalog.GetProductAsync(stored.Id))!.Variants).Id);
    }

    [Fact]
    public async Task Keeps_variant_added_after_editor_was_opened()
    {
        var first = new ProductVariant { Color = "A", Size = "M", Quantity = 2 };
        var stored = new Product { Name = "Футболка", Sku = "TS-7", Variants = [first] };
        var (service, catalog, _) = Create(stored);
        var original = Copy(stored);
        var edited = Copy(stored);
        var received = new ProductVariant { Color = "B", Size = "L", Quantity = 4 };
        var current = (await catalog.GetProductAsync(stored.Id))!;
        current.Variants.Add(received);
        await catalog.UpsertProductAsync(current);
        edited.Name = "Футболка оверсайз";

        await service.SaveAsync(edited, original);

        var actual = (await catalog.GetProductAsync(stored.Id))!;
        Assert.Equal("Футболка оверсайз", actual.Name);
        Assert.Equal(4, Assert.Single(actual.Variants, x => x.Id == received.Id).Quantity);
    }

    [Fact]
    public async Task Keeps_prices_changed_by_receipt_while_editor_was_open()
    {
        var stored = new Product { Name = "Футболка", Sku = "TS-8", PurchasePriceCny = 30, SellingPriceUzs = 100_000, Variants = [new ProductVariant { Quantity = 1 }] };
        var (service, catalog, _) = Create(stored);
        var original = Copy(stored);
        var edited = Copy(stored);
        var current = (await catalog.GetProductAsync(stored.Id))!;
        current.PurchasePriceCny = 35;
        await catalog.UpsertProductAsync(current);
        edited.SellingPriceUzs = 120_000;

        await service.SaveAsync(edited, original);

        var actual = (await catalog.GetProductAsync(stored.Id))!;
        Assert.Equal(35, actual.PurchasePriceCny);
        Assert.Equal(120_000, actual.SellingPriceUzs);
    }

    [Fact]
    public async Task Rejects_duplicate_sku_and_recalculates_status()
    {
        var other = new Product { Name = "Другой", Sku = "DUP-1" };
        var stored = new Product { Name = "Футболка", Sku = "TS-5", Status = ProductStatus.OnOrder, Variants = [new ProductVariant { Quantity = 20 }] };
        var (service, catalog, _) = Create(stored);
        await catalog.UpsertProductAsync(other);
        var edited = Copy(stored);
        edited.Sku = " dup-1 ";

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(edited));

        edited.Sku = "TS-5";
        await service.SaveAsync(edited);
        Assert.Equal(ProductStatus.InStock, (await catalog.GetProductAsync(stored.Id))!.Status);
    }

    [Fact]
    public async Task Archive_keeps_stock_and_sets_archived_status()
    {
        var stored = new Product { Name = "Футболка", Sku = "TS-6", Variants = [new ProductVariant { Quantity = 5, ReservedQuantity = 1 }] };
        var (service, catalog, _) = Create(stored);

        await service.ArchiveAsync(Copy(stored));

        var actual = (await catalog.GetProductAsync(stored.Id))!;
        Assert.Equal(ProductStatus.Archived, actual.Status);
        Assert.Equal(5, actual.Variants[0].Quantity);
        Assert.Equal(1, actual.Variants[0].ReservedQuantity);
    }
}
