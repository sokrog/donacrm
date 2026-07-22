using Dona.Crm.Storage.Sqlite;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;
using SQLite;

namespace Dona.Crm.Storage.Sqlite.Tests;

public sealed class SqliteRepositoryTests : IAsyncLifetime
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "dona-crm-sqlite-tests", Guid.NewGuid().ToString("N"));
    private SqliteAggregateStore? store;

    private string DatabasePath => Path.Combine(directory, "test.db3");

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(directory);
        store = new SqliteAggregateStore(new SqliteStoreOptions(DatabasePath));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (store is not null)
            await store.DisposeAsync();

        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task Catalog_seeds_once_and_preserves_nested_product_data()
    {
        var repository = new SqliteCatalogRepository(store!);
        var seeded = await repository.GetProductsAsync();
        Assert.Equal(3, seeded.Count);

        var product = new Product
        {
            Sku = "DR-TEST",
            Name = "Платье Test",
            Status = ProductStatus.InStock,
            Variants = [new ProductVariant { Color = "Чёрный", Size = "S", Quantity = 4 }],
            Images = [new ProductImage { FileName = "dress.jpg", Url = "local://dress.jpg", IsMain = true }]
        };

        await repository.UpsertProductAsync(product);
        await store!.CloseAsync();

        var reopened = new SqliteCatalogRepository(store);
        var actual = await reopened.GetProductAsync(product.Id);

        Assert.NotNull(actual);
        Assert.Equal(4, actual.Quantity);
        Assert.Equal("local://dress.jpg", actual.PrimaryImageUrl);
    }

    [Fact]
    public async Task Deleting_every_product_does_not_restore_seed_data()
    {
        var repository = new SqliteCatalogRepository(store!);
        foreach (var product in await repository.GetProductsAsync())
            await repository.DeleteProductAsync(product.Id);

        Assert.Empty(await repository.GetProductsAsync());
    }

    [Fact]
    public async Task Sales_repository_round_trips_payments_and_items()
    {
        var repository = new SqliteSalesRepository(store!);
        var sale = new Sale
        {
            Number = "SALE-TEST-001",
            Status = SaleStatus.Completed,
            Items = [new SaleItem { ProductName = "Худи Minimal", Quantity = 2, UnitPriceUzs = 279_000, SoldQuantity = 2 }],
            Payments = [new SalePayment { Type = PaymentOperationType.Payment, Status = PaymentStatus.Completed, Method = PaymentMethod.Click, AmountUzs = 558_000 }]
        };

        await repository.UpsertSaleAsync(sale);
        var actual = await repository.GetSaleAsync(sale.Id);

        Assert.NotNull(actual);
        Assert.Equal(558_000, actual.TotalUzs);
        Assert.Equal(558_000, actual.PaidUzs);
        Assert.Equal(2, actual.TotalQuantity);
    }

    [Fact]
    public async Task Database_uses_wal_journal_mode()
    {
        _ = await new SqliteCatalogRepository(store!).GetProductsAsync();
        await store!.CloseAsync();

        using var connection = new SQLiteConnection(DatabasePath);
        var mode = connection.ExecuteScalar<string>("PRAGMA journal_mode;");

        Assert.Equal("wal", mode, ignoreCase: true);
    }

    [Fact]
    public async Task Marketing_delete_clears_references_from_content_posts()
    {
        var repository = new SqliteMarketingRepository(store!);
        var collection = new ProductCollection { Name = "Summer" };
        var post = new ContentPost { Title = "Launch", CollectionId = collection.Id, CollectionName = collection.Name };

        await repository.UpsertCollectionAsync(collection);
        await repository.UpsertContentPostAsync(post);
        await repository.DeleteCollectionAsync(collection.Id);

        var actual = Assert.Single(await repository.GetContentPostsAsync());
        Assert.Null(actual.CollectionId);
        Assert.Null(actual.CollectionName);
    }

    [Fact]
    public async Task Marketing_repository_round_trips_outfit_products_and_prices()
    {
        var repository = new SqliteMarketingRepository(store!);
        var outfit = new Outfit
        {
            Name = "Городской образ",
            Status = MarketingStatus.Active,
            Products =
            [
                new OutfitProduct { ProductName = "Худи", SellingPriceUzs = 279_000, SortOrder = 0 },
                new OutfitProduct { ProductName = "Сумка", SellingPriceUzs = 169_000, SortOrder = 1 }
            ]
        };

        await repository.UpsertOutfitAsync(outfit);
        await store!.CloseAsync();

        var actual = Assert.Single(await new SqliteMarketingRepository(store).GetOutfitsAsync());
        Assert.Equal(448_000, actual.TotalPriceUzs);
        Assert.Equal(["Худи", "Сумка"], actual.Products.OrderBy(item => item.SortOrder).Select(item => item.ProductName));
    }

    [Fact]
    public async Task Marketing_repository_round_trips_complete_content_plan_document()
    {
        var repository = new SqliteMarketingRepository(store!);
        var collection = new ProductCollection { Name = "Осень" };
        var outfit = new Outfit { Name = "Городской образ" };
        var post = new ContentPost
        {
            Title = "Новый дроп",
            Type = ContentType.Reels,
            Status = ContentStatus.Planned,
            ScheduledAt = new DateTime(2026, 8, 5),
            CollectionId = collection.Id,
            CollectionName = collection.Name,
            OutfitId = outfit.Id,
            OutfitName = outfit.Name,
            Caption = "Показываем новый образ",
            PublicationUrl = "https://example.test/post",
            Notes = "Подготовить обложку"
        };

        await repository.UpsertCollectionAsync(collection);
        await repository.UpsertOutfitAsync(outfit);
        await repository.UpsertContentPostAsync(post);
        await store!.CloseAsync();

        var actual = Assert.Single(await new SqliteMarketingRepository(store).GetContentPostsAsync());
        Assert.Equal(ContentType.Reels, actual.Type);
        Assert.Equal(ContentStatus.Planned, actual.Status);
        Assert.Equal(new DateTime(2026, 8, 5), actual.ScheduledAt);
        Assert.Equal(collection.Id, actual.CollectionId);
        Assert.Equal(outfit.Id, actual.OutfitId);
        Assert.Equal("Показываем новый образ", actual.Caption);
        Assert.Equal("https://example.test/post", actual.PublicationUrl);
        Assert.Equal("Подготовить обложку", actual.Notes);
    }

    [Fact]
    public async Task Supporting_repositories_persist_settings_stock_and_purchase_history()
    {
        var settings = new SqliteBusinessSettingsRepository(store!);
        await settings.SaveAsync(new BusinessSettings { LowStockThreshold = 7, SaleNumberPrefix = "CO" });

        var stock = new SqliteStockMovementRepository(store!);
        var movement = new StockMovement { ProductName = "Test", QuantityDelta = 3 };
        await stock.AddRangeAsync([movement]);

        var history = new SqlitePurchaseHistoryRepository(store!);
        var cost = new ProductCostHistoryEntry { Id = Guid.NewGuid(), ProductName = "Test", UnitPriceCny = 15 };
        var rate = new ExchangeRateHistoryEntry { Id = Guid.NewGuid(), RateUzs = 1_800 };
        await history.AddAsync([cost], rate);
        await history.AddAsync([cost], rate);

        Assert.Equal(7, (await settings.GetAsync()).LowStockThreshold);
        Assert.Equal(movement.Id, Assert.Single(await stock.GetAsync()).Id);
        Assert.Single((await history.GetAsync()).ProductCosts);
        Assert.Single((await history.GetAsync()).ExchangeRates);
    }

    [Fact]
    public void Home_dashboard_calculates_local_first_signals()
    {
        var now = new DateTimeOffset(2026, 7, 22, 14, 0, 0, TimeSpan.FromHours(5));
        var product = new Product
        {
            Name = "Худи",
            Status = ProductStatus.LowStock,
            Variants = [new ProductVariant { Quantity = 3, ReservedQuantity = 1 }]
        };
        var sale = new Sale
        {
            Number = "SALE-1",
            CreatedAt = now.AddHours(-1),
            Status = SaleStatus.Completed,
            Items = [new SaleItem { Quantity = 1, SoldQuantity = 1, UnitPriceUzs = 200_000, UnitCostUzs = 120_000 }],
            Payments = [new SalePayment { Type = PaymentOperationType.Payment, Status = PaymentStatus.Completed, AmountUzs = 150_000 }]
        };
        var cancelled = new Sale
        {
            CreatedAt = now,
            Status = SaleStatus.Cancelled,
            Items = [new SaleItem { Quantity = 1, UnitPriceUzs = 999_000 }]
        };
        var purchase = new Purchase
        {
            Status = PurchaseStatus.Shipped,
            EstimatedDeliveryDate = now.Date,
            Items = [new PurchaseItem { Quantity = 5, ReceivedQuantity = 2 }]
        };

        var actual = HomeDashboardService.Calculate(
            [product], [purchase], [sale, cancelled], new BusinessSettings { LowStockThreshold = 3 }, [], now);

        Assert.Equal(200_000, actual.TodayRevenueUzs);
        Assert.Equal(80_000, actual.TodayProfitUzs);
        Assert.Equal(50_000, actual.CustomerDebtUzs);
        Assert.Equal(2, actual.AvailableQuantity);
        Assert.Equal(3, actual.IncomingQuantity);
        Assert.Single(actual.LowStockProducts);
        Assert.Single(actual.SalesWithDebt);
        Assert.Single(actual.DuePurchases);
    }

    [Fact]
    public async Task Stock_adjustment_updates_status_and_writes_movement_atomically_for_the_workflow()
    {
        var catalog = new SqliteCatalogRepository(store!);
        var movements = new SqliteStockMovementRepository(store!);
        var settings = new SqliteBusinessSettingsRepository(store!);
        await settings.SaveAsync(new BusinessSettings { LowStockThreshold = 3, AutoUpdateStockStatus = true });
        var product = (await catalog.GetProductsAsync()).Single(item => item.Status == ProductStatus.LowStock);
        var variant = Assert.Single(product.Variants);
        var service = new StockAdjustmentService(catalog, movements, settings, new ProductStatusService());

        await service.AdjustAsync(new StockAdjustmentRequest
        {
            ProductId = product.Id,
            ProductVariantId = variant.Id,
            NewQuantity = 8,
            Reason = StockAdjustmentReason.InventoryCount,
            Note = "Контрольный пересчёт"
        });

        Assert.Equal(ProductStatus.InStock, (await catalog.GetProductAsync(product.Id))!.Status);
        var movement = Assert.Single(await movements.GetAsync());
        Assert.Equal(6, movement.QuantityDelta);
        Assert.Equal("Контрольный пересчёт", movement.Note);
    }

    [Fact]
    public async Task Sales_workflow_reserves_accepts_payment_and_completes_against_sqlite()
    {
        var catalog = new SqliteCatalogRepository(store!);
        var sales = new SqliteSalesRepository(store!);
        var movements = new SqliteStockMovementRepository(store!);
        var product = (await catalog.GetProductsAsync()).First(item => item.Variants.Any(variant => variant.AvailableQuantity >= 1));
        var variant = product.Variants.First(item => item.AvailableQuantity >= 1);
        var originalQuantity = variant.Quantity!.Value;
        var sale = new Sale
        {
            Number = "SALE-SQLITE-001",
            Status = SaleStatus.Draft,
            Items = [new SaleItem { ProductId = product.Id, ProductVariantId = variant.Id, ProductName = product.Name, Color = variant.Color, Size = variant.Size, Quantity = 1, UnitPriceUzs = product.SellingPriceUzs }]
        };
        var inventory = new SalesInventoryService(catalog, sales, movements);

        await inventory.ReserveAsync(sale);
        Assert.Equal(SaleStatus.Reserved, sale.Status);
        Assert.Equal(1, (await catalog.GetProductAsync(product.Id))!.Variants.Single(item => item.Id == variant.Id).ReservedQuantity);

        await new SalesPaymentService(sales).AddAsync(sale, new SalePayment
        {
            Type = PaymentOperationType.Payment,
            Status = PaymentStatus.Completed,
            Method = PaymentMethod.Cash,
            AmountUzs = sale.TotalUzs
        });
        Assert.Equal(SaleStatus.Paid, sale.Status);

        await inventory.CompleteAsync(sale);
        var completedProduct = await catalog.GetProductAsync(product.Id);
        Assert.Equal(SaleStatus.Completed, sale.Status);
        Assert.Equal(originalQuantity - 1, completedProduct!.Variants.Single(item => item.Id == variant.Id).Quantity);
        Assert.Contains(await movements.GetAsync(), item => item.Type == StockMovementType.Reservation);
        Assert.Contains(await movements.GetAsync(), item => item.Type == StockMovementType.Sale);

        await new SalesReturnService(catalog, sales, movements).CreateAsync(sale, new SaleReturn
        {
            Reason = "Не подошёл размер",
            RefundAmountUzs = sale.TotalUzs,
            Items = [new SaleReturnItem { SaleItemId = sale.Items[0].Id, Quantity = 1, Disposition = ReturnDisposition.Restock }]
        });
        Assert.Equal(SaleStatus.Returned, sale.Status);
        Assert.Equal(originalQuantity, (await catalog.GetProductAsync(product.Id))!.Variants.Single(item => item.Id == variant.Id).Quantity);
        Assert.Contains(await movements.GetAsync(), item => item.Type == StockMovementType.Return);

        await new SalesPaymentService(sales).AddAsync(sale, new SalePayment
        {
            Type = PaymentOperationType.Refund,
            Status = PaymentStatus.Completed,
            Method = PaymentMethod.Cash,
            AmountUzs = sale.RefundDueUzs
        });
        Assert.Equal(0, sale.RefundDueUzs);
        Assert.Equal(0, sale.PaidUzs);
    }

    [Fact]
    public async Task Customer_deletion_keeps_the_sale_snapshot_in_history()
    {
        var repository = new SqliteSalesRepository(store!);
        var customer = new Customer { Name = "Дилноза", Phone = "+998 90 123 45 67" };
        var sale = new Sale
        {
            Number = "SALE-CUSTOMER-001",
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            Status = SaleStatus.Draft
        };

        await repository.UpsertCustomerAsync(customer);
        await repository.UpsertSaleAsync(sale);
        await repository.DeleteCustomerAsync(customer.Id);

        Assert.Empty(await repository.GetCustomersAsync());
        var historicalSale = Assert.Single(await repository.GetSalesAsync());
        Assert.Equal(customer.Id, historicalSale.CustomerId);
        Assert.Equal("Дилноза", historicalSale.CustomerName);
    }

    [Fact]
    public async Task Purchase_receiving_updates_stock_movements_and_cost_history_in_sqlite()
    {
        var catalog = new SqliteCatalogRepository(store!);
        var commerce = new SqliteCommerceRepository(store!);
        var movements = new SqliteStockMovementRepository(store!);
        var history = new SqlitePurchaseHistoryRepository(store!);
        var product = (await catalog.GetProductsAsync()).First();
        var variant = product.Variants.First();
        var originalQuantity = variant.Quantity ?? 0;
        var purchase = new Purchase
        {
            Number = "PO-SQLITE-001",
            Status = PurchaseStatus.Shipped,
            CurrencyCode = "CNY",
            CnyRateUzs = 1_800,
            Items =
            [
                new PurchaseItem
                {
                    ProductId = product.Id,
                    ProductVariantId = variant.Id,
                    ProductName = product.Name,
                    Color = variant.Color,
                    Size = variant.Size,
                    Quantity = 3,
                    UnitPriceCny = 20
                }
            ]
        };
        await commerce.UpsertPurchaseAsync(purchase);
        var service = new PurchaseReceivingService(catalog, commerce, movements, history);

        await service.ReceiveAsync(purchase, [new PurchaseReceiptInput(purchase.Items[0].Id, 2, 1)]);

        Assert.Equal(PurchaseStatus.PartiallyReceived, purchase.Status);
        Assert.Equal(originalQuantity + 1, (await catalog.GetProductAsync(product.Id))!.Variants.Single(item => item.Id == variant.Id).Quantity);
        Assert.Single(await movements.GetAsync());
        Assert.Single((await history.GetAsync()).ProductCosts);

        await service.ReceiveAsync(purchase, [new PurchaseReceiptInput(purchase.Items[0].Id, 1, 0)]);

        Assert.Equal(PurchaseStatus.Received, purchase.Status);
        Assert.Equal(originalQuantity + 2, (await catalog.GetProductAsync(product.Id))!.Variants.Single(item => item.Id == variant.Id).Quantity);
        Assert.Equal(2, (await movements.GetAsync()).Count);
        Assert.Equal(2, (await history.GetAsync()).ProductCosts.Count);
    }

    [Fact]
    public async Task Commerce_partners_round_trip_and_deletion_preserves_purchase_snapshots()
    {
        var repository = new SqliteCommerceRepository(store!);
        var supplier = new Supplier { Name = "1688 Store", Platform = "1688", Moq = 5, WeChat = "store-cn" };
        var intermediary = new Intermediary { Name = "Cargo One", RatePerKgUsd = 7.5m, OfficialImport = true };
        var purchase = new Purchase
        {
            Number = "PO-PARTNERS-001",
            SupplierId = supplier.Id,
            SupplierName = supplier.Name,
            IntermediaryId = intermediary.Id,
            IntermediaryName = intermediary.Name
        };

        await repository.UpsertSupplierAsync(supplier);
        await repository.UpsertIntermediaryAsync(intermediary);
        await repository.UpsertPurchaseAsync(purchase);

        Assert.Equal("store-cn", Assert.Single(await repository.GetSuppliersAsync()).WeChat);
        Assert.True(Assert.Single(await repository.GetIntermediariesAsync()).OfficialImport);

        await repository.DeleteSupplierAsync(supplier.Id);
        await repository.DeleteIntermediaryAsync(intermediary.Id);

        Assert.Empty(await repository.GetSuppliersAsync());
        Assert.Empty(await repository.GetIntermediariesAsync());
        var historicalPurchase = Assert.Single(await repository.GetPurchasesAsync());
        Assert.Equal("1688 Store", historicalPurchase.SupplierName);
        Assert.Equal("Cargo One", historicalPurchase.IntermediaryName);
    }
}
