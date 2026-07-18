using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;
using Dona.Crm.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace Dona.Crm.Web.Tests;

public sealed class ProductEconomicsTests
{
    [Fact]
    public void Product_search_matches_sku_and_name_case_insensitively()
    {
        var products = new[] { new Product { Sku = "TS-001", Name = "Белая футболка" }, new Product { Sku = "BG-002", Name = "City Bag" } };
        Assert.Equal("TS-001", Assert.Single(ProductSearch.Filter(products, "ts-001")).Sku);
        Assert.Equal("BG-002", Assert.Single(ProductSearch.Filter(products, "city")).Sku);
        Assert.Empty(ProductSearch.Filter(products, "худи"));
    }

    [Fact]
    public void New_product_has_no_prefilled_form_values()
    {
        var product = new Product();
        Assert.Empty(product.Name);
        Assert.Empty(product.Category);
        Assert.Null(product.Status);
        Assert.Null(product.CnyRateUzs);
        Assert.Null(product.SellingPriceUzs);
    }

    [Fact]
    public void Calculates_cost_profit_and_markup()
    {
        var product = new Product
        {
            PurchasePriceCny = 28,
            CnyRateUzs = 1_800,
            AgentCommissionPercent = 5,
            DeliveryCostUzs = 18_000,
            SellingPriceUzs = 119_000
        };

        Assert.Equal(70_920, product.CostUzs);
        Assert.Equal(48_080, product.ProfitUzs);
        Assert.Equal(67.8m, product.MarkupPercent);
    }

    [Fact]
    public void Calculates_stock_from_variants()
    {
        var product = new Product
        {
            Variants =
            [
                new ProductVariant { Quantity = 3, ReservedQuantity = 1 },
                new ProductVariant { Quantity = 5, ReservedQuantity = 2 }
            ]
        };

        Assert.Equal(8, product.Quantity);
        Assert.Equal(2, product.Variants[0].AvailableQuantity);
        Assert.Equal(3, product.Variants[1].AvailableQuantity);
    }
}

public sealed class PurchaseEconomicsTests
{
    [Fact]
    public void Calculates_complete_purchase_cost()
    {
        var purchase = new Purchase
        {
            CnyRateUzs = 1_800,
            AgentCommissionPercent = 5,
            InternationalShippingUzs = 100_000,
            OtherCostsUzs = 20_000,
            Items = [new PurchaseItem { ProductName = "Футболка", Quantity = 10, UnitPriceCny = 25 }]
        };

        Assert.Equal(250, purchase.GoodsCostCny);
        Assert.Equal(450_000, purchase.GoodsCostUzs);
        Assert.Equal(22_500, purchase.AgentCommissionUzs);
        Assert.Equal(592_500, purchase.TotalCostUzs);
        Assert.Equal(10, purchase.TotalQuantity);
    }

    [Fact]
    public void Allocates_shipping_by_weight_and_calculates_receipt_quantities()
    {
        var light = new PurchaseItem { ProductName = "Лёгкий", Quantity = 10, UnitPriceCny = 10, UnitWeightKg = 0.1m, ReceivedQuantity = 9, DefectQuantity = 1 };
        var heavy = new PurchaseItem { ProductName = "Тяжёлый", Quantity = 10, UnitPriceCny = 10, UnitWeightKg = 0.3m };
        var purchase = new Purchase { CnyRateUzs = 1_000, InternationalShippingUzs = 40_000, Items = [light, heavy] };

        Assert.Equal(10_000, purchase.ItemShippingUzs(light));
        Assert.Equal(30_000, purchase.ItemShippingUzs(heavy));
        Assert.Equal(1, light.MissingQuantity);
        Assert.Equal(8, light.AcceptedQuantity);
        Assert.Equal(8, light.QuantityToStock);
    }
}

public sealed class PurchaseReceivingServiceTests
{
    [Fact]
    public async Task Stores_partial_receipts_and_does_not_add_the_same_receipt_twice()
    {
        var variant = new ProductVariant { Color = "Черный", Size = "M", Quantity = 2 };
        var product = new Product { Name = "Футболка", Sku = "TS-1", Variants = [variant] };
        var item = new PurchaseItem { ProductId = product.Id, ProductVariantId = variant.Id, ProductName = product.Name, Color = variant.Color, Size = variant.Size, Quantity = 10, UnitPriceCny = 20 };
        var supplierId = Guid.NewGuid();
        var purchase = new Purchase { Number = "PO-1", SupplierId = supplierId, SupplierName = "1688 Store", CnyRateUzs = 1_800, AgentCommissionPercent = 5, InternationalShippingUzs = 90_000, Items = [item] };
        var catalog = new MemoryCatalog(product);
        var commerce = new MemoryCommerce();
        var movements = new MemoryStockMovements();
        var purchaseHistory = new MemoryPurchaseHistory();
        var service = new PurchaseReceivingService(catalog, commerce, movements, purchaseHistory);

        var firstReceiptId = Guid.NewGuid();
        var first = await service.ReceiveAsync(purchase, [new PurchaseReceiptInput(item.Id, 8, 1)], firstReceiptId);
        var duplicate = await service.ReceiveAsync(purchase, [new PurchaseReceiptInput(item.Id, 8, 1)], firstReceiptId);

        Assert.Equal(7, first.AddedUnits);
        Assert.Equal(7, duplicate.AddedUnits);
        Assert.Equal(9, variant.Quantity);
        Assert.Equal(7, item.StockedQuantity);
        Assert.Equal(PurchaseStatus.PartiallyReceived, purchase.Status);
        Assert.Single(purchase.Receipts);

        var second = await service.ReceiveAsync(purchase, [new PurchaseReceiptInput(item.Id, 2, 0)], Guid.NewGuid());
        Assert.Equal(2, second.AddedUnits);
        Assert.Equal(11, variant.Quantity);
        Assert.Equal(9, item.StockedQuantity);
        Assert.Equal(PurchaseStatus.Received, purchase.Status);
        Assert.Equal(2, purchase.Receipts.Count);
        var history = await movements.GetAsync();
        Assert.Collection(history,
            x => { Assert.Equal(StockMovementType.PurchaseReceipt, x.Type); Assert.Equal(7, x.QuantityDelta); },
            x => { Assert.Equal(StockMovementType.PurchaseReceipt, x.Type); Assert.Equal(2, x.QuantityDelta); });
        var costs = await purchaseHistory.GetAsync();
        Assert.Equal(2, costs.ProductCosts.Count);
        Assert.Equal(2, costs.ExchangeRates.Count);
        Assert.All(costs.ProductCosts, x => { Assert.Equal("TS-1", x.Sku); Assert.Equal("1688 Store", x.SupplierName); Assert.Equal(1_800, x.CnyRateUzs); Assert.True(x.UnitLandedCostUzs > 0); });
        Assert.Equal(7, costs.ProductCosts[0].Quantity);
        Assert.Equal(2, costs.ProductCosts[1].Quantity);
    }

    [Fact]
    public async Task Manual_adjustment_requires_a_reason_and_cannot_go_below_reserve()
    {
        var variant = new ProductVariant { Color = "Черный", Size = "M", Quantity = 10, ReservedQuantity = 3 };
        var product = new Product { Name = "Худи", Sku = "HD-1", Variants = [variant] };
        var movements = new MemoryStockMovements();
        var service = new StockAdjustmentService(new MemoryCatalog(product), movements);
        var request = new StockAdjustmentRequest { ProductId = product.Id, ProductVariantId = variant.Id, Reason = StockAdjustmentReason.InventoryCount, NewQuantity = 7, Note = "Фактический пересчёт" };

        await service.AdjustAsync(request);

        Assert.Equal(7, variant.Quantity);
        var movement = Assert.Single(await movements.GetAsync());
        Assert.Equal(-3, movement.QuantityDelta);
        Assert.Equal("Фактический пересчёт", movement.Note);
        request.NewQuantity = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AdjustAsync(request));
    }

    private sealed class MemoryCatalog(Product product) : ICatalogRepository
    {
        public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Product>>([product]);
        public Task<Product?> GetProductAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Product?>(id == product.Id ? product : null);
        public Task UpsertProductAsync(Product value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteProductAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class MemoryCommerce : ICommerceRepository
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
        public Task<IReadOnlyList<Purchase>> GetPurchasesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Purchase>>([]);
        public Task<Purchase?> GetPurchaseAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Purchase?>(null);
        public Task UpsertPurchaseAsync(Purchase purchase, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class MemoryPurchaseHistory : IPurchaseHistoryRepository
    {
        private readonly PurchaseHistoryData _data = new();
        public Task<PurchaseHistoryData> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(_data);
        public Task AddAsync(IEnumerable<ProductCostHistoryEntry> productCosts, ExchangeRateHistoryEntry? exchangeRate, CancellationToken cancellationToken = default)
        {
            var ids = _data.ProductCosts.Select(x => x.Id).ToHashSet();
            _data.ProductCosts.AddRange(productCosts.Where(x => ids.Add(x.Id)));
            if (exchangeRate is not null && _data.ExchangeRates.All(x => x.Id != exchangeRate.Id)) _data.ExchangeRates.Add(exchangeRate);
            return Task.CompletedTask;
        }
    }
}

public sealed class SupplierAnalyticsServiceTests
{
    [Fact]
    public void Calculates_quality_completeness_delivery_and_product_comparison()
    {
        var supplier = new Supplier { Name = "Store A" };
        var productId = Guid.NewGuid();
        var purchase = new Purchase
        {
            Number = "PO-10", SupplierId = supplier.Id, SupplierName = supplier.Name, Status = PurchaseStatus.Received,
            EstimatedDeliveryDate = new DateTime(2026, 7, 10),
            Items = [new PurchaseItem { ProductId = productId, ProductName = "Футболка", Quantity = 10 }],
            Receipts = [new PurchaseReceipt { ReceivedAt = new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.FromHours(5)), Lines = [new PurchaseReceiptLine { ProductId = productId, ReceivedQuantity = 9, DefectQuantity = 1, StockedQuantity = 8 }] }]
        };
        var secondSupplier = new Supplier { Name = "Store B" };
        var history = new PurchaseHistoryData { ProductCosts =
        [
            new ProductCostHistoryEntry { ProductId = productId, ProductName = "Футболка", Sku = "TS-1", SupplierId = supplier.Id, SupplierName = supplier.Name, ReceiptId = Guid.NewGuid(), Quantity = 8, UnitPriceCny = 20, UnitLandedCostUzs = 50_000 },
            new ProductCostHistoryEntry { ProductId = productId, ProductName = "Футболка", Sku = "TS-1", SupplierId = secondSupplier.Id, SupplierName = secondSupplier.Name, ReceiptId = Guid.NewGuid(), Quantity = 5, UnitPriceCny = 18, UnitLandedCostUzs = 47_000 }
        ] };
        var service = new SupplierAnalyticsService();

        var analytics = Assert.Single(service.Calculate([supplier], [purchase], history));
        Assert.Equal(11.1m, analytics.DefectRatePercent);
        Assert.Equal(90m, analytics.CompletenessPercent);
        Assert.Equal(2m, analytics.AverageDelayDays);
        Assert.Equal(0m, analytics.OnTimePercent);
        Assert.Equal(50_000m, analytics.AverageUnitCostUzs);
        Assert.Equal(74m, analytics.ReliabilityScore);
        var comparison = service.CompareProducts(history);
        Assert.Equal(2, comparison.Count);
        Assert.Equal(47_000m, comparison.Single(x => x.SupplierId == secondSupplier.Id).AverageUnitCostUzs);
    }
}

public sealed class IntermediaryAnalyticsServiceTests
{
    [Fact]
    public void Calculates_actual_shipping_cost_transit_delay_and_reliability()
    {
        var intermediary = new Intermediary { Name = "Cargo A", Rating = 4.5m, EstimatedDays = 10 };
        var purchase = new Purchase
        {
            Number = "PO-CARGO", IntermediaryId = intermediary.Id, IntermediaryName = intermediary.Name, Status = PurchaseStatus.Received,
            OrderedAt = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.FromHours(5)), EstimatedDeliveryDate = new DateTime(2026, 7, 11), InternationalShippingUzs = 200_000,
            Items = [new PurchaseItem { ProductName = "Худи", Quantity = 10, UnitWeightKg = 0.5m }],
            Receipts = [new PurchaseReceipt { ReceivedAt = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.FromHours(5)) }]
        };

        var analytics = Assert.Single(new IntermediaryAnalyticsService().Calculate([intermediary], [purchase]));

        Assert.Equal(5m, analytics.TotalWeightKg);
        Assert.Equal(40_000m, analytics.AverageShippingPerKgUzs);
        Assert.Equal(12m, analytics.AverageTransitDays);
        Assert.Equal(2m, analytics.AverageDelayDays);
        Assert.Equal(0m, analytics.OnTimePercent);
        Assert.Equal(90m, analytics.ReliabilityScore);
    }
}

public sealed class InventoryAnalyticsServiceTests
{
    [Fact]
    public void Calculates_inventory_value_turnover_cover_and_stale_stock()
    {
        var variant = new ProductVariant { Color = "Чёрный", Size = "M", Quantity = 10, ReservedQuantity = 2 };
        var product = new Product { Name = "Футболка", Sku = "TS-1", Category = "Футболки", PurchasePriceCny = 10, CnyRateUzs = 1_000, DeliveryCostUzs = 1_000, SellingPriceUzs = 25_000, CreatedAt = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.FromHours(5)), Variants = [variant] };
        var staleVariant = new ProductVariant { Color = "Белый", Size = "L", Quantity = 5 };
        var stale = new Product { Name = "Худи", Sku = "HD-1", Category = "Худи", PurchasePriceCny = 5, CnyRateUzs = 1_000, SellingPriceUzs = 15_000, CreatedAt = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.FromHours(5)), Variants = [staleVariant] };
        var sale = new Sale { Status = SaleStatus.Completed, CreatedAt = new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.FromHours(5)), Items = [new SaleItem { ProductId = product.Id, ProductVariantId = variant.Id, SoldQuantity = 2 }] };
        var receipt = new StockMovement { Type = StockMovementType.PurchaseReceipt, ProductId = product.Id, ProductVariantId = variant.Id, CreatedAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(5)), QuantityDelta = 10 };

        var report = new InventoryAnalyticsService().Build([product, stale], [sale], [receipt], new BusinessSettings { StaleInventoryDays = 60, LowStockThreshold = 3 }, new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.FromHours(5)));

        Assert.Equal(15, report.PhysicalUnits);
        Assert.Equal(2, report.ReservedUnits);
        Assert.Equal(13, report.AvailableUnits);
        Assert.Equal(135_000m, report.InventoryCostUzs);
        var active = report.Rows.Single(x => x.ProductId == product.Id);
        Assert.Equal(2, active.Sold30Days);
        Assert.Equal(360m, active.DaysOfCover);
        Assert.False(active.IsStale);
        var staleRow = report.Rows.Single(x => x.ProductId == stale.Id);
        Assert.True(staleRow.IsStale);
        Assert.Equal(25_000m, report.StaleInventoryCostUzs);
    }
}

public sealed class ProfitAnalyticsServiceTests
{
    [Fact]
    public void Attributes_profit_to_purchase_source_collections_outfits_and_published_content()
    {
        var product = new Product { Name = "Футболка", Sku = "TS-1", SupplierName = "Fallback" };
        var unsold = new Product { Name = "Сумка", Sku = "BG-1" };
        var purchase = new Purchase { SupplierName = "Store A", IntermediaryName = "Cargo A", Items = [new PurchaseItem { ProductId = product.Id, ProductName = product.Name }], Receipts = [new PurchaseReceipt { ReceivedAt = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(5)), Lines = [new PurchaseReceiptLine { ProductId = product.Id }] }] };
        var sale = new Sale { Status = SaleStatus.Completed, CreatedAt = new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.FromHours(5)), Items = [new SaleItem { ProductId = product.Id, ProductName = product.Name, Quantity = 2, SoldQuantity = 2, UnitPriceUzs = 100, UnitCostUzs = 50 }] };
        var collection = new ProductCollection { Name = "Лето", Products = [new CollectionProduct { ProductId = product.Id }, new CollectionProduct { ProductId = unsold.Id }] };
        var outfit = new Outfit { Name = "Образ 1", Products = [new OutfitProduct { ProductId = product.Id }] };
        var marketing = new MarketingData { Collections = [collection], Outfits = [outfit], ContentPosts = [new ContentPost { Title = "Летняя публикация", Status = ContentStatus.Published, Type = ContentType.Reels, ScheduledAt = new DateTime(2026, 7, 5), CollectionId = collection.Id }] };

        var report = new ProfitAnalyticsService().Build([sale], [product, unsold], [purchase], marketing, null, null);

        var supplier = Assert.Single(report.Suppliers);
        Assert.Equal("Store A", supplier.Name);
        Assert.Equal(100m, supplier.ProfitUzs);
        Assert.Equal("Cargo A", Assert.Single(report.Intermediaries).Name);
        Assert.Equal("Лето", Assert.Single(report.Collections).Name);
        Assert.Equal("Образ 1", Assert.Single(report.Outfits).Name);
        var content = Assert.Single(report.Content);
        Assert.Equal(2, content.Products);
        Assert.Equal(1, content.ProductsWithoutSales);
        Assert.Equal(200m, content.RevenueUzs);
    }
}

public sealed class JsonCatalogRepositoryTests
{
    [Fact]
    public async Task Persists_a_product_and_reads_it_with_a_new_repository_instance()
    {
        var root = Path.Combine(Path.GetTempPath(), "dona-crm-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var environment = new TestEnvironment(root);
            var options = Options.Create(new StorageOptions { DataFile = "data/catalog.json" });
            var firstRepository = new JsonCatalogRepository(environment, options);
            var product = new Product { Sku = "TEST-001", Name = "Тестовый товар", SellingPriceUzs = 99_000 };

            await firstRepository.UpsertProductAsync(product);

            var secondRepository = new JsonCatalogRepository(environment, options);
            var restored = await secondRepository.GetProductAsync(product.Id);
            Assert.NotNull(restored);
            Assert.Equal("TEST-001", restored.Sku);
            Assert.Equal(99_000, restored.SellingPriceUzs);
            await secondRepository.DeleteProductAsync(product.Id);
            Assert.Null(await secondRepository.GetProductAsync(product.Id));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TestEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Dona.Crm.Web.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

public sealed class SalesInventoryServiceTests
{
    [Fact]
    public async Task Reserve_complete_and_return_are_idempotent()
    {
        var variant = new ProductVariant { Color = "Черный", Size = "M", Quantity = 10 };
        var product = new Product { Name = "Футболка", Sku = "TS-2", SellingPriceUzs = 120_000, Variants = [variant] };
        var item = new SaleItem { ProductId = product.Id, ProductVariantId = variant.Id, ProductName = product.Name, Color = variant.Color, Size = variant.Size, Quantity = 3, UnitPriceUzs = 120_000 };
        var sale = new Sale { Number = "SALE-1", Items = [item] };
        var catalog = new SalesMemoryCatalog(product);
        var sales = new MemorySales();
        var movements = new MemoryStockMovements();
        var service = new SalesInventoryService(catalog, sales, movements);

        await service.ReserveAsync(sale);
        await service.ReserveAsync(sale);
        Assert.Equal(3, variant.ReservedQuantity);
        Assert.Equal(3, item.ReservedQuantity);

        await service.CompleteAsync(sale);
        await service.CompleteAsync(sale);
        Assert.Equal(7, variant.Quantity);
        Assert.Equal(0, variant.ReservedQuantity);
        Assert.Equal(3, item.SoldQuantity);
        Assert.Equal(SaleStatus.Completed, sale.Status);

        await service.ReturnAsync(sale);
        await service.ReturnAsync(sale);
        Assert.Equal(10, variant.Quantity);
        Assert.Equal(3, item.ReturnedQuantity);
        Assert.Equal(SaleStatus.Returned, sale.Status);
        var history = await movements.GetAsync();
        Assert.Collection(history,
            x => { Assert.Equal(StockMovementType.Reservation, x.Type); Assert.Equal(3, x.ReservedDelta); },
            x => { Assert.Equal(StockMovementType.Sale, x.Type); Assert.Equal(-3, x.QuantityDelta); Assert.Equal(-3, x.ReservedDelta); },
            x => { Assert.Equal(StockMovementType.Return, x.Type); Assert.Equal(3, x.QuantityDelta); });
    }

    [Fact]
    public async Task Cancelling_releases_reservation_without_changing_stock()
    {
        var variant = new ProductVariant { Quantity = 5 };
        var product = new Product { Name = "Сумка", Sku = "BG-2", Variants = [variant] };
        var sale = new Sale { Number = "SALE-2", Items = [new SaleItem { ProductId = product.Id, ProductVariantId = variant.Id, ProductName = product.Name, Quantity = 2, UnitPriceUzs = 100 }] };
        var service = new SalesInventoryService(new SalesMemoryCatalog(product), new MemorySales());

        await service.ReserveAsync(sale);
        await service.CancelAsync(sale);

        Assert.Equal(5, variant.Quantity);
        Assert.Equal(0, variant.ReservedQuantity);
        Assert.Equal(0, sale.Items[0].ReservedQuantity);
        Assert.Equal(SaleStatus.Cancelled, sale.Status);
    }

    [Fact]
    public async Task Rejects_quantity_above_available_stock_without_mutation()
    {
        var variant = new ProductVariant { Color = "Черный", Size = "L", Quantity = 2 };
        var product = new Product { Name = "Худи", Sku = "HD-3", Variants = [variant] };
        var sale = new Sale { Number = "SALE-3", Items = [new SaleItem { ProductId = product.Id, ProductVariantId = variant.Id, ProductName = product.Name, Color = variant.Color, Size = variant.Size, Quantity = 3, UnitPriceUzs = 200 }] };
        var service = new SalesInventoryService(new SalesMemoryCatalog(product), new MemorySales());

        var error = await Assert.ThrowsAsync<InventoryException>(() => service.ReserveAsync(sale));

        Assert.Contains("Доступно 2", error.Message);
        Assert.Equal(2, variant.Quantity);
        Assert.Equal(0, variant.ReservedQuantity);
        Assert.Null(sale.Status);
    }

    [Fact]
    public async Task Rejects_duplicate_variant_lines()
    {
        var variant = new ProductVariant { Quantity = 10 };
        var product = new Product { Name = "Ремень", Sku = "BL-1", Variants = [variant] };
        SaleItem Line() => new() { ProductId = product.Id, ProductVariantId = variant.Id, ProductName = product.Name, Quantity = 1, UnitPriceUzs = 50 };
        var sale = new Sale { Number = "SALE-4", Items = [Line(), Line()] };
        var service = new SalesInventoryService(new SalesMemoryCatalog(product), new MemorySales());

        await Assert.ThrowsAsync<InventoryException>(() => service.ReserveAsync(sale));
        Assert.Equal(0, variant.ReservedQuantity);
    }

    [Fact]
    public async Task Enforces_status_transitions_and_keeps_reservation_synced()
    {
        var variant = new ProductVariant { Quantity = 5 };
        var product = new Product { Name = "Кепка", Sku = "CP-1", Variants = [variant] };
        var item = new SaleItem { ProductId = product.Id, ProductVariantId = variant.Id, ProductName = product.Name, Quantity = 2, UnitPriceUzs = 100 };
        var sale = new Sale { Number = "SALE-5", Items = [item] };
        var service = new SalesInventoryService(new SalesMemoryCatalog(product), new MemorySales());

        await Assert.ThrowsAsync<SaleTransitionException>(() => service.CompleteAsync(sale));
        await service.ReserveAsync(sale);
        item.Quantity = 4;
        await service.ReserveAsync(sale);
        Assert.Equal(4, variant.ReservedQuantity);
        await service.MarkPaidAsync(sale);
        Assert.Equal(SaleStatus.Paid, sale.Status);
        await service.MarkShippedAsync(sale);
        Assert.Equal(SaleStatus.Shipped, sale.Status);
        await Assert.ThrowsAsync<SaleTransitionException>(() => service.CancelAsync(sale));
        await service.CompleteAsync(sale);

        Assert.Equal(1, variant.Quantity);
        Assert.Equal(0, variant.ReservedQuantity);
        Assert.Equal(4, item.SoldQuantity);
        Assert.Equal(SaleStatus.Completed, sale.Status);
    }

    [Fact]
    public async Task Partial_returns_restock_only_saleable_items_and_update_net_economics()
    {
        var variant = new ProductVariant { Quantity = 7 };
        var product = new Product { Name = "Футболка", Sku = "TS-R", Variants = [variant] };
        var item = new SaleItem { ProductId = product.Id, ProductVariantId = variant.Id, ProductName = product.Name, Quantity = 3, SoldQuantity = 3, UnitPriceUzs = 100, UnitCostUzs = 40 };
        var sale = new Sale { Number = "SALE-R", Status = SaleStatus.Completed, Items = [item] };
        var movements = new MemoryStockMovements();
        var service = new SalesReturnService(new SalesMemoryCatalog(product), new MemorySales(), movements);
        var first = new SaleReturn { Reason = "Не подошёл размер", RefundAmountUzs = 100, Items = [new SaleReturnItem { SaleItemId = item.Id, Quantity = 1, Disposition = ReturnDisposition.Restock }] };

        await service.CreateAsync(sale, first);
        await service.CreateAsync(sale, first);

        Assert.Equal(8, variant.Quantity);
        Assert.Equal(1, item.ReturnedQuantity);
        Assert.Equal(SaleStatus.Completed, sale.Status);
        Assert.Equal(200, sale.NetTotalUzs);
        Assert.Equal(80, sale.CostUzs);
        Assert.Single(sale.Returns);

        var second = new SaleReturn { Reason = "Повреждение", RefundAmountUzs = 200, Items = [new SaleReturnItem { SaleItemId = item.Id, Quantity = 2, Disposition = ReturnDisposition.Defect }] };
        await service.CreateAsync(sale, second);

        Assert.Equal(8, variant.Quantity);
        Assert.Equal(3, item.ReturnedQuantity);
        Assert.Equal(SaleStatus.Returned, sale.Status);
        Assert.Equal(0, sale.NetTotalUzs);
        Assert.Equal(80, sale.CostUzs);
        var movement = Assert.Single(await movements.GetAsync());
        Assert.Equal(1, movement.QuantityDelta);
        Assert.Equal("SaleReturn", movement.SourceType);
    }

    [Fact]
    public async Task Multiple_payments_close_debt_and_money_refund_tracks_return_document()
    {
        var sale = new Sale { Number = "SALE-P", Status = SaleStatus.Reserved, Items = [new SaleItem { ProductName = "Худи", Quantity = 3, UnitPriceUzs = 100 }] };
        var service = new SalesPaymentService(new MemorySales());
        var first = new SalePayment { Type = PaymentOperationType.Payment, Status = PaymentStatus.Completed, Method = PaymentMethod.Payme, AmountUzs = 100 };

        await service.AddAsync(sale, first);
        await service.AddAsync(sale, first);
        Assert.Equal(100, sale.PaidUzs);
        Assert.Equal(200, sale.BalanceDueUzs);
        Assert.Equal(SaleStatus.Reserved, sale.Status);
        Assert.Single(sale.Payments);

        await service.AddAsync(sale, new SalePayment { Type = PaymentOperationType.Payment, Status = PaymentStatus.Completed, Method = PaymentMethod.Cash, AmountUzs = 200 });
        Assert.Equal(300, sale.PaidUzs);
        Assert.Equal(0, sale.BalanceDueUzs);
        Assert.Equal(SaleStatus.Paid, sale.Status);

        sale.Returns.Add(new SaleReturn { Reason = "Размер", RefundAmountUzs = 100 });
        await service.AddAsync(sale, new SalePayment { Type = PaymentOperationType.Refund, Status = PaymentStatus.Completed, Method = PaymentMethod.Cash, AmountUzs = 100 });
        Assert.Equal(200, sale.PaidUzs);
        Assert.Equal(0, sale.RefundDueUzs);
        Assert.Equal(3, sale.Payments.Count);
    }

    [Fact]
    public void New_sale_and_customer_have_no_prefilled_form_values()
    {
        var sale = new Sale();
        var customer = new Customer();
        Assert.Empty(sale.Number);
        Assert.Null(sale.Status);
        Assert.Null(sale.CustomerId);
        Assert.Empty(sale.Items);
        Assert.Empty(customer.Name);
        Assert.Null(customer.Phone);
    }

    private sealed class SalesMemoryCatalog(Product product) : ICatalogRepository
    {
        public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Product>>([product]);
        public Task<Product?> GetProductAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Product?>(id == product.Id ? product : null);
        public Task UpsertProductAsync(Product value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteProductAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class MemorySales : ISalesRepository
    {
        public Task<IReadOnlyList<Customer>> GetCustomersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Customer>>([]);
        public Task UpsertCustomerAsync(Customer customer, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteCustomerAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Sale>> GetSalesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Sale>>([]);
        public Task<Sale?> GetSaleAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Sale?>(null);
        public Task UpsertSaleAsync(Sale sale, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

public sealed class MarketingTests
{
    [Fact]
    public void New_marketing_forms_have_no_prefilled_values_and_outfit_sums_prices()
    {
        var collection = new ProductCollection();
        var post = new ContentPost();
        var outfit = new Outfit { Products = [new OutfitProduct { SellingPriceUzs = 120_000 }, new OutfitProduct { SellingPriceUzs = 80_000 }] };

        Assert.Empty(collection.Name);
        Assert.Null(collection.Status);
        Assert.Empty(collection.Products);
        Assert.Empty(post.Title);
        Assert.Null(post.Type);
        Assert.Null(post.ScheduledAt);
        Assert.Equal(200_000, outfit.TotalPriceUzs);
    }

    [Fact]
    public async Task Json_repository_persists_marketing_data_and_clears_deleted_links()
    {
        var root = Path.Combine(Path.GetTempPath(), "dona-crm-marketing-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var repository = new JsonMarketingRepository(new MarketingEnvironment(root));
            var collection = new ProductCollection { Name = "Лето" };
            var outfit = new Outfit { Name = "Travel" };
            var post = new ContentPost { Title = "Карусель", CollectionId = collection.Id, CollectionName = collection.Name, OutfitId = outfit.Id, OutfitName = outfit.Name };
            await repository.UpsertCollectionAsync(collection);
            await repository.UpsertOutfitAsync(outfit);
            await repository.UpsertContentPostAsync(post);

            var restored = await new JsonMarketingRepository(new MarketingEnvironment(root)).GetDataAsync();
            Assert.Single(restored.Collections);
            Assert.Single(restored.Outfits);
            Assert.Single(restored.ContentPosts);

            await repository.DeleteCollectionAsync(collection.Id);
            await repository.DeleteOutfitAsync(outfit.Id);
            var unlinked = Assert.Single((await repository.GetDataAsync()).ContentPosts);
            Assert.Null(unlinked.CollectionId);
            Assert.Null(unlinked.OutfitId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class MarketingEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Dona.Crm.Web.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

public sealed class AnalyticsServiceTests
{
    [Fact]
    public void Builds_period_totals_and_excludes_returns()
    {
        var product = new Product { Name = "Футболка", Category = "Одежда" };
        var completed = new Sale
        {
            Number = "S-1", CustomerName = "Алина", Status = SaleStatus.Completed,
            CreatedAt = new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero), DiscountUzs = 30, DeliveryChargeUzs = 10,
            Items =
            [
                new SaleItem { ProductId = product.Id, ProductName = product.Name, Quantity = 2, SoldQuantity = 2, UnitPriceUzs = 100, UnitCostUzs = 40 },
                new SaleItem { ProductName = "Сумка", Quantity = 1, SoldQuantity = 1, UnitPriceUzs = 100, UnitCostUzs = 50 }
            ]
        };
        var returned = new Sale { Number = "S-2", Status = SaleStatus.Returned, CreatedAt = completed.CreatedAt, Items = [new SaleItem { Quantity = 1, SoldQuantity = 1, UnitPriceUzs = 1_000, UnitCostUzs = 100 }] };

        var report = new AnalyticsService().Build([completed, returned], [product], new DateTime(2026, 7, 1), new DateTime(2026, 7, 31));

        Assert.Equal(1, report.Orders);
        Assert.Equal(3, report.Units);
        Assert.Equal(280, report.RevenueUzs);
        Assert.Equal(130, report.CostUzs);
        Assert.Equal(150, report.ProfitUzs);
        Assert.Single(report.Daily);
        Assert.Equal("Одежда", report.Categories[0].Name);
        Assert.Equal("Алина", report.Customers[0].Name);
    }

    [Fact]
    public void Date_filter_is_inclusive()
    {
        var sale = new Sale { Number = "S-3", Status = SaleStatus.Completed, CreatedAt = new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero) };
        var service = new AnalyticsService();
        Assert.Equal(1, service.Build([sale], [], new DateTime(2026, 7, 17), new DateTime(2026, 7, 17)).Orders);
        Assert.Equal(0, service.Build([sale], [], new DateTime(2026, 7, 18), null).Orders);
    }

    [Fact]
    public void Partial_return_reduces_revenue_units_and_only_restocked_cost()
    {
        var item = new SaleItem { ProductName = "Футболка", Quantity = 3, SoldQuantity = 3, ReturnedQuantity = 1, UnitPriceUzs = 100, UnitCostUzs = 40 };
        var sale = new Sale { Number = "S-R", Status = SaleStatus.Completed, Items = [item], Returns = [new SaleReturn { RefundAmountUzs = 100, Reason = "Размер", Items = [new SaleReturnItem { SaleItemId = item.Id, Quantity = 1, Disposition = ReturnDisposition.Restock }] }] };

        var report = new AnalyticsService().Build([sale], [], null, null);

        Assert.Equal(2, report.Units);
        Assert.Equal(200, report.RevenueUzs);
        Assert.Equal(80, report.CostUzs);
        Assert.Equal(120, report.ProfitUzs);
    }

    [Fact]
    public void Documented_full_defect_return_keeps_loss_in_analytics()
    {
        var item = new SaleItem { ProductName = "Сумка", Quantity = 2, SoldQuantity = 2, ReturnedQuantity = 2, UnitPriceUzs = 100, UnitCostUzs = 30 };
        var sale = new Sale { Number = "S-LOSS", Status = SaleStatus.Returned, Items = [item], Returns = [new SaleReturn { RefundAmountUzs = 200, Reason = "Брак", Items = [new SaleReturnItem { SaleItemId = item.Id, Quantity = 2, Disposition = ReturnDisposition.Defect }] }] };

        var report = new AnalyticsService().Build([sale], [], null, null);

        Assert.Equal(1, report.Orders);
        Assert.Equal(0, report.RevenueUzs);
        Assert.Equal(60, report.CostUzs);
        Assert.Equal(-60, report.ProfitUzs);
    }
}

public sealed class ProductStatusServiceTests
{
    [Fact]
    public void Calculates_status_from_available_stock_and_threshold()
    {
        var product = new Product { Status = ProductStatus.InStock, Variants = [new ProductVariant { Quantity = 5, ReservedQuantity = 2 }] };
        var service = new ProductStatusService();

        Assert.Equal(ProductStatus.LowStock, service.Calculate(product, new BusinessSettings { LowStockThreshold = 3, CountReservedAsUnavailable = true }));
        Assert.Equal(ProductStatus.InStock, service.Calculate(product, new BusinessSettings { LowStockThreshold = 2, CountReservedAsUnavailable = true }));
        Assert.Equal(ProductStatus.InStock, service.Calculate(product, new BusinessSettings { LowStockThreshold = 3, CountReservedAsUnavailable = false }));
    }

    [Fact]
    public void Uses_configured_zero_status_and_preserves_manual_exceptions()
    {
        var service = new ProductStatusService();
        var empty = new Product { Status = ProductStatus.InStock, Variants = [new ProductVariant { Quantity = 0 }] };
        Assert.Equal(ProductStatus.OutOfStock, service.Calculate(empty, new BusinessSettings()));
        Assert.Equal(ProductStatus.OnOrder, service.Calculate(empty, new BusinessSettings { ZeroStockStatus = ProductStatus.OnOrder }));

        var archived = new Product { Status = ProductStatus.Archived, Variants = [new ProductVariant { Quantity = 10 }] };
        var preorder = new Product { Status = ProductStatus.OnOrder };
        Assert.Equal(ProductStatus.Archived, service.Calculate(archived, new BusinessSettings()));
        Assert.Equal(ProductStatus.OnOrder, service.Calculate(preorder, new BusinessSettings()));
    }

    [Fact]
    public void Restored_product_returns_to_stock_status_rules()
    {
        var service = new ProductStatusService();
        var product = new Product { Status = ProductStatus.OnOrder, Variants = [new ProductVariant { Quantity = 5 }] };
        Assert.Equal(ProductStatus.InStock, service.Calculate(product, new BusinessSettings { LowStockThreshold = 3 }));

        var withoutVariants = new Product { Status = ProductStatus.OnOrder };
        Assert.Equal(ProductStatus.OnOrder, service.Calculate(withoutVariants, new BusinessSettings()));
    }
}

public sealed class GoogleSheetsSettingsStoreTests
{
    [Fact]
    public async Task Saves_url_as_spreadsheet_id_and_persists_credentials()
    {
        var root = Path.Combine(Path.GetTempPath(), "dona-crm-settings-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var environment = new SettingsEnvironment(root);
            var options = Options.Create(new GoogleSheetsOptions());
            var store = new GoogleSheetsSettingsStore(environment, options);
            const string id = "1AbCdEfGhIjKlMnOpQrStUvWxYz";
            const string credentials = """{"type":"service_account","client_email":"catalog@test.iam.gserviceaccount.com","private_key":"secret"}""";

            await store.SaveAsync($"https://docs.google.com/spreadsheets/d/{id}/edit", credentials);

            Assert.Equal(id, store.SpreadsheetId);
            Assert.Equal("catalog@test.iam.gserviceaccount.com", store.ServiceAccountEmail);
            Assert.True(store.IsConfigured);
            var reloaded = new GoogleSheetsSettingsStore(environment, options);
            Assert.Equal(id, reloaded.SpreadsheetId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class SettingsEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Dona.Crm.Web.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

internal sealed class MemoryStockMovements : IStockMovementRepository
{
    private readonly List<StockMovement> _items = [];
    public Task<IReadOnlyList<StockMovement>> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StockMovement>>(_items);
    public Task AddRangeAsync(IEnumerable<StockMovement> movements, CancellationToken cancellationToken = default) { _items.AddRange(movements); return Task.CompletedTask; }
}

public sealed class LoadingStateTests
{
    [Fact]
    public void Fullscreen_phase_finishes_once_and_operation_count_remains_nested()
    {
        var state = new LoadingState();
        Assert.True(state.IsInitialLoad);
        using (state.Begin("Первая"))
        {
            using (state.Begin("Вторая")) Assert.True(state.IsLoading);
            Assert.True(state.IsLoading);
        }
        Assert.False(state.IsLoading);
        state.CompleteInitialLoad();
        Assert.False(state.IsInitialLoad);
        using (state.Begin("Следующая")) Assert.True(state.IsLoading);
        Assert.False(state.IsInitialLoad);
    }
}
