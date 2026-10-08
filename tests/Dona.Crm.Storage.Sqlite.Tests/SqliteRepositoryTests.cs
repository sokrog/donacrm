using Dona.Crm.Storage.Sqlite;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;
using Dona.Crm.Web.Storage;
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
    public async Task Partial_late_receipt_keeps_remaining_shortage_and_compensation_balanced()
    {
        var catalog = new SqliteCatalogRepository(store!);
        var commerce = new SqliteCommerceRepository(store!);
        var service = new PurchaseReceivingService(catalog, new SqliteInventoryStore(store!), commerce);
        var product = new Product { Name = "Товар", Sku = "LATE-PART" };
        await catalog.UpsertProductAsync(product);
        var purchase = new Purchase { Number = "P-LATE-PART", CurrencyCode = "UZS",
            Items = [new() { ProductId = product.Id, ProductName = product.Name, Quantity = 10, UnitPriceCny = 100 }] };
        await service.SaveAsync(purchase);
        await service.ReceiveAsync(purchase, [new(purchase.Items[0].Id, 6, 0)]);
        await service.CloseAsync(purchase, [new(purchase.Items[0].Id, 100)], Guid.NewGuid());
        var settlement = Assert.Single(purchase.ShortageSettlements);
        await service.ReceiveLateAsync(purchase.Id, settlement.Id, Guid.NewGuid(), 4, 2, 0, "Часть недостачи найдена");
        var saved = (await commerce.GetPurchaseAsync(purchase.Id))!;
        Assert.Equal(2, saved.UnresolvedShortage(settlement));
        Assert.Equal(100m, saved.StockValuations.Sum(x => x.ExpenseDelta));
        saved.Expenses.Add(new() { Name = "Доставка", Amount = 100 });
        await service.SaveAsync(saved);
        Assert.Equal(120m, saved.StockValuations.Sum(x => x.ExpenseDelta));
        Assert.Equal(880m, FifoCostCalculator.Value((await catalog.GetProductAsync(product.Id))!.Variants[0]).TotalValue);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReceiveLateAsync(purchase.Id, settlement.Id, Guid.NewGuid(), 4, 1, 0, "Устаревшая форма"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Late_receipt_reverses_shortage_and_survives_revaluation(int defects)
    {
        var catalog = new SqliteCatalogRepository(store!);
        var commerce = new SqliteCommerceRepository(store!);
        var service = new PurchaseReceivingService(catalog, new SqliteInventoryStore(store!), commerce);
        var product = new Product { Name = "Товар", Sku = "LATE" };
        await catalog.UpsertProductAsync(product);
        var purchase = new Purchase { Number = "P-LATE", CurrencyCode = "UZS",
            Items = [new() { ProductId = product.Id, ProductName = product.Name, Quantity = 10, UnitPriceCny = 100 }] };
        await service.SaveAsync(purchase);
        await service.ReceiveAsync(purchase, [new(purchase.Items[0].Id, 8, 0)]);
        await service.CloseAsync(purchase, [new(purchase.Items[0].Id, 120)], Guid.NewGuid());
        var settlement = Assert.Single(purchase.ShortageSettlements);
        var operation = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReceiveLateAsync(purchase.Id, settlement.Id, operation, 2, 2, defects, "Нашли посылку"));
        Assert.Empty((await commerce.GetPurchaseAsync(purchase.Id))!.LateReceipts);
        await service.CorrectCompensationAsync(purchase.Id, settlement.Id, Guid.NewGuid(), 120, 0, "Компенсация возвращена");
        var result = await service.ReceiveLateAsync(purchase.Id, settlement.Id, operation, 2, 2, defects, "Нашли посылку");
        Assert.Equal(result, await service.ReceiveLateAsync(purchase.Id, settlement.Id, operation, 2, 2, defects, "Нашли посылку"));
        var saved = (await commerce.GetPurchaseAsync(purchase.Id))!;
        Assert.Equal(settlement, Assert.Single(saved.ShortageSettlements));
        Assert.Equal(purchase.ClosedAt, saved.ClosedAt);
        Assert.Single(saved.LateReceipts);
        Assert.Equal(0, saved.UnresolvedShortage(settlement));
        Assert.Equal(10, saved.Items[0].ReceivedQuantity);
        Assert.Equal(PurchaseStatus.Received, saved.Status);
        var variant = (await catalog.GetProductAsync(product.Id))!.Variants[0];
        Assert.Equal(10 - defects, variant.Quantity);
        Assert.Equal(defects == 2 ? 200m : 0m, saved.StockValuations.Sum(x => x.ExpenseDelta));
        saved.Expenses.Add(new() { Name = "Поздний расход", Amount = 100 });
        await service.SaveAsync(saved);
        await service.SaveAsync(saved);
        variant = (await catalog.GetProductAsync(product.Id))!.Variants[0];
        Assert.Equal(defects == 2 ? 880m : 1100m, FifoCostCalculator.Value(variant).TotalValue);
        Assert.Equal(defects == 2 ? 220m : 0m, saved.StockValuations.Sum(x => x.ExpenseDelta));
        Assert.Equal(1100m, FifoCostCalculator.Value(variant).TotalValue + saved.StockValuations.Sum(x => x.ExpenseDelta));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReceiveLateAsync(purchase.Id, settlement.Id, Guid.NewGuid(), 0, 1, 0, "Лишнее"));
    }

    [Fact]
    public async Task Closed_purchase_compensation_correction_is_dated_idempotent_and_preserves_settlement()
    {
        var catalog = new SqliteCatalogRepository(store!);
        var commerce = new SqliteCommerceRepository(store!);
        var service = new PurchaseReceivingService(catalog, new SqliteInventoryStore(store!), commerce);
        var product = new Product { Name = "Товар", Sku = "REFUND" };
        await catalog.UpsertProductAsync(product);
        var purchase = new Purchase { Number = "P-REFUND", CurrencyCode = "UZS",
            Items = [new() { ProductId = product.Id, ProductName = product.Name, Quantity = 10, UnitPriceCny = 100 }] };
        await service.SaveAsync(purchase);
        await service.ReceiveAsync(purchase, [new(purchase.Items[0].Id, 8, 0)]);
        await service.CloseAsync(purchase, [new(purchase.Items[0].Id, 120)], Guid.NewGuid());
        var settlement = Assert.Single(purchase.ShortageSettlements);
        var oldDay = DateTimeOffset.Now.AddMonths(-1);
        purchase.StockValuations[0] = purchase.StockValuations[0] with { RecognizedAt = oldDay };
        await commerce.UpsertPurchaseAsync(purchase);
        var operation = Guid.NewGuid();
        var correction = await service.CorrectCompensationAsync(purchase.Id, settlement.Id, operation, 120, 170, "Доплата поставщика");
        Assert.Equal(correction, await service.CorrectCompensationAsync(purchase.Id, settlement.Id, operation, 120, 170, "Доплата поставщика"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CorrectCompensationAsync(purchase.Id, settlement.Id, operation, 120, 180, "Доплата поставщика"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CorrectCompensationAsync(purchase.Id, settlement.Id, Guid.NewGuid(), 120, 180, "Устаревшая форма"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CorrectCompensationAsync(purchase.Id, settlement.Id, Guid.NewGuid(), 170, 201, "Слишком много"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(purchase));
        var saved = (await commerce.GetPurchaseAsync(purchase.Id))!;
        Assert.Equal(settlement, Assert.Single(saved.ShortageSettlements));
        Assert.Single(saved.CompensationCorrections);
        Assert.Equal(170m, saved.CurrentRefund(settlement));
        Assert.Equal(30m, saved.StockValuations.Sum(x => x.ExpenseDelta));
        var analytics = new AnalyticsService();
        Assert.Equal(80m, analytics.Build([], [], oldDay.Date, oldDay.Date, [saved]).PeriodExpensesUzs);
        Assert.Equal(-50m, analytics.Build([], [], DateTime.Today, DateTime.Today, [saved]).PeriodExpensesUzs);
        // A later expense must adjust only shortage cost, without charging the compensation twice.
        saved.Expenses.Add(new() { Name = "Поздний расход", Amount = 100 });
        await service.SaveAsync(saved);
        Assert.Equal(50m, saved.StockValuations.Sum(x => x.ExpenseDelta));
        await service.CorrectCompensationAsync(purchase.Id, settlement.Id, Guid.NewGuid(), 170, 150, "Исправление суммы");
        saved = (await commerce.GetPurchaseAsync(purchase.Id))!;
        Assert.Equal(70m, saved.StockValuations.Sum(x => x.ExpenseDelta));
        Assert.Equal(150m, saved.CurrentRefund(settlement));
        Assert.Equal(1100m, 880m + saved.CurrentRefund(settlement) + saved.StockValuations.Sum(x => x.ExpenseDelta));
        saved.Items[0].UnitPriceCny = 1;
        saved.Expenses.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(saved));
        Assert.Equal(100m, (await commerce.GetPurchaseAsync(purchase.Id))!.Items[0].UnitPriceCny);
    }

    [Fact]
    public async Task Initial_layer_valuation_survives_restart_and_replay()
    {
        var catalog = new SqliteCatalogRepository(store!);
        var product = CreateProduct(quantity: 0);
        var variant = product.Variants[0];
        var layer = StockLayerOperations.Add(variant, 3, null, StockLayerSource.InventorySurplus, DateTimeOffset.UtcNow, "Излишек");
        FifoCostCalculator.Consume(variant, 1);
        await catalog.UpsertProductAsync(product);
        var request = new InitialStockValuationRequest(product.Id, variant.Id, layer.Id, Guid.NewGuid(), 0, 2, 10);
        var value = await new StockValuationService(catalog, new SqliteInventoryStore(store!)).ValueAsync(request);
        await store!.DisposeAsync();
        store = new SqliteAggregateStore(new SqliteStoreOptions(DatabasePath));
        catalog = new SqliteCatalogRepository(store);
        Assert.Equal(value, await new StockValuationService(catalog, new SqliteInventoryStore(store)).ValueAsync(request));
        var saved = (await catalog.GetProductAsync(product.Id))!;
        Assert.Single(saved.StockValuations);
        Assert.Equal(6.67m, saved.Variants[0].Layers[0].RemainingValue);
        Assert.Equal(3.33m, saved.StockValuations[0].ExpenseDelta);
    }

    [Fact]
    public async Task Shortage_closure_partial_refund_late_expense_and_replay_reconcile()
    {
        var catalog = new SqliteCatalogRepository(store!);
        var commerce = new SqliteCommerceRepository(store!);
        var product = new Product { Name = "Товар", Sku = "SHORT" };
        await catalog.UpsertProductAsync(product);
        var purchase = new Purchase { Number = "P-SHORT", CurrencyCode = "UZS",
            Items = [new() { ProductId = product.Id, ProductName = product.Name, Quantity = 10, UnitPriceCny = 100 }] };
        var service = new PurchaseReceivingService(catalog, new SqliteInventoryStore(store!), commerce);
        await service.SaveAsync(purchase);
        await service.ReceiveAsync(purchase, [new(purchase.Items[0].Id, 8, 0)]);
        var operation = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CloseAsync(purchase, [new(purchase.Items[0].Id, 201)], operation));
        Assert.Null(purchase.ClosedAt);
        Assert.Empty(purchase.ShortageSettlements);
        Assert.Empty(purchase.StockValuations);
        await service.CloseAsync(purchase, [new(purchase.Items[0].Id, 120)], operation);
        await service.CloseAsync(purchase, [new(purchase.Items[0].Id, 120)], operation);
        var settlement = Assert.Single(purchase.ShortageSettlements);
        Assert.Equal(200m, settlement.AllocatedCost);
        Assert.Equal(80m, settlement.Loss);
        Assert.Equal(80m, Assert.Single(purchase.StockValuations).ExpenseDelta);
        Assert.NotNull((await commerce.GetPurchaseAsync(purchase.Id))!.ClosedAt);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReceiveAsync(purchase, [new(purchase.Items[0].Id, 2, 0)]));
        purchase.Expenses.Add(new() { Name = "Поздний расход", Amount = 100 });
        await service.SaveAsync(purchase);
        await service.SaveAsync(purchase);
        Assert.Equal(100m, purchase.StockValuations.Where(x => x.Reason == StockValuationReason.Shortage).Sum(x => x.ExpenseDelta));
        var remaining = FifoCostCalculator.Value((await catalog.GetProductAsync(product.Id))!.Variants.Single());
        Assert.Equal(880m, remaining.TotalValue);
        Assert.Equal(1100m, remaining.TotalValue + settlement.SupplierRefund + purchase.StockValuations.Sum(x => x.ExpenseDelta));
    }

    [Fact]
    public async Task Complete_defect_late_cost_remains_a_loss_without_stock_layer()
    {
        var catalog = new SqliteCatalogRepository(store!);
        var commerce = new SqliteCommerceRepository(store!);
        var product = new Product { Name = "Брак", Sku = "DEFECT" };
        await catalog.UpsertProductAsync(product);
        var purchase = new Purchase { Number = "P-DEF", CurrencyCode = "UZS",
            Items = [new() { ProductId = product.Id, ProductName = product.Name, Quantity = 10, UnitPriceCny = 100 }] };
        var service = new PurchaseReceivingService(catalog, new SqliteInventoryStore(store!), commerce);
        await service.SaveAsync(purchase);
        await service.ReceiveAsync(purchase, [new(purchase.Items[0].Id, 10, 10)]);
        purchase.Expenses.Add(new() { Name = "Доставка", Amount = 200 });
        await service.SaveAsync(purchase);
        await service.SaveAsync(purchase);
        Assert.Equal(1200m, purchase.StockValuations.Sum(x => x.ExpenseDelta));
        Assert.Equal(2, purchase.StockValuations.Count);
        Assert.Empty((await catalog.GetProductAsync(product.Id))!.Variants.Single().Layers);
    }

    [Fact]
    public async Task Late_cost_revalues_stock_and_return_reverses_sold_adjustment()
    {
        var catalog = new SqliteCatalogRepository(store!);
        var commerce = new SqliteCommerceRepository(store!);
        var inventoryStore = new SqliteInventoryStore(store!);
        var product = new Product { Name = "FIFO", Sku = "REVALUE" };
        await catalog.UpsertProductAsync(product);
        var purchase = new Purchase { Number = "P", CurrencyCode = "UZS", Items = [new() { ProductId = product.Id, ProductName = product.Name, Quantity = 10, UnitPriceCny = 100 }] };
        var receiving = new PurchaseReceivingService(catalog, inventoryStore, commerce);
        await receiving.SaveAsync(purchase);
        await receiving.ReceiveAsync(purchase, [new(purchase.Items[0].Id, 10, 0)]);
        var variant = (await catalog.GetProductAsync(product.Id))!.Variants.Single();
        var sale = new Sale { Number = "S", Items = [new() { ProductId = product.Id, ProductVariantId = variant.Id, Quantity = 6, UnitPriceUzs = 250 }] };
        var saleRepository = new SqliteSalesRepository(store!);
        var selling = new SalesInventoryService(catalog, inventoryStore, sales: saleRepository);
        await selling.ReserveAsync(sale);
        var staleSale = (await saleRepository.GetSaleAsync(sale.Id))!;
        await selling.CompleteAsync(sale);
        await Assert.ThrowsAsync<InventoryException>(() => selling.CompleteAsync(staleSale));
        purchase.Expenses.Add(new() { Name = "Доставка", Amount = 200 });
        await receiving.SaveAsync(purchase);
        await receiving.SaveAsync(purchase);
        var valuation = Assert.Single(purchase.StockValuations);
        Assert.Equal(80m, valuation.InventoryValueDelta);
        Assert.Equal(120m, valuation.ExpenseDelta);
        Assert.Equal(600m, sale.CostUzs);
        await new SalesReturnService(catalog, inventoryStore, saleRepository).CreateAsync(sale, new()
        {
            Reason = "Размер", RefundAmountUzs = 250,
            Items = [new() { SaleItemId = sale.Items[0].Id, Quantity = 1, Disposition = ReturnDisposition.Restock }]
        });
        var actual = (await catalog.GetProductAsync(product.Id))!;
        Assert.Equal(600m, FifoCostCalculator.Value(actual.Variants.Single()).TotalValue);
        Assert.Equal(-20m, Assert.Single(actual.StockValuations).ExpenseDelta);
        Assert.Equal(500m, sale.CostUzs);
        var resale = new Sale { Items = [new() { ProductId = product.Id, ProductVariantId = variant.Id, Quantity = 1, UnitPriceUzs = 250 }] };
        await selling.ReserveAsync(resale);
        await selling.CompleteAsync(resale);
        Assert.Equal(120m, resale.CostUzs);
    }

    [Fact]
    public async Task Clean_start_and_opening_balance_preserve_quantity_value_and_single_movement_after_restart()
    {
        var catalog = new SqliteCatalogRepository(store!);
        Assert.Empty(await catalog.GetProductsAsync());
        var service = new ProductEditingService(catalog, new SqliteSalesRepository(store!), new SqliteCommerceRepository(store!),
            new SqliteBusinessSettingsRepository(store!), new ProductStatusService(), new SqliteInventoryStore(store!));
        var product = new Product { Sku = "OPEN", Name = "Товар", PurchaseCurrencyCode = "UZS", PurchasePriceCny = 100,
            Variants = [new() { Color = "Белый", Quantity = 2 }, new() { Color = "Чёрный", Quantity = 0 }] };
        var saved = await service.SaveAsync(product);
        await service.SaveAsync(saved);
        await store!.CloseAsync();
        saved = (await catalog.GetProductAsync(product.Id))!;
        Assert.All(saved.Variants, x => Assert.Equal(FifoCostCalculator.CurrentVersion, x.StockLayerVersion));
        Assert.Single(saved.Variants[0].Layers);
        Assert.Empty(saved.Variants[1].Layers);
        Assert.Equal(200m, FifoCostCalculator.Value(saved.Variants[0]).TotalValue);
        var movement = Assert.Single(await new SqliteStockMovementRepository(store).GetAsync());
        Assert.Equal(2, movement.QuantityDelta);
        Assert.Equal(200m, movement.ValueDelta);
        Assert.Equal(0, movement.UnvaluedQuantity);
    }

    [Fact]
    public async Task Concurrent_completion_of_two_snapshots_consumes_only_once()
    {
        var catalog = new SqliteCatalogRepository(store!);
        var sales = new SqliteSalesRepository(store!);
        var product = CreateProduct(quantity: 5);
        await catalog.UpsertProductAsync(product);
        var service = new SalesInventoryService(catalog, new SqliteInventoryStore(store!), sales: sales);
        var sale = new Sale { Items = [new() { ProductId = product.Id, ProductVariantId = product.Variants[0].Id, Quantity = 3, UnitPriceUzs = 100 }] };
        await service.ReserveAsync(sale);
        var other = (await sales.GetSaleAsync(sale.Id))!;
        async Task<bool> Complete(Sale document)
        {
            try { await service.CompleteAsync(document); return true; }
            catch (InventoryException) { return false; }
        }
        var results = await Task.WhenAll(Complete(sale), Complete(other));
        Assert.Single(results, x => x);
        var actual = (await catalog.GetProductAsync(product.Id))!.Variants[0];
        Assert.Equal(2, actual.Quantity);
        Assert.Equal(0, actual.ReservedQuantity);
        Assert.Equal(3, (await sales.GetSaleAsync(sale.Id))!.Items[0].Consumptions.Sum(x => x.Quantity));
    }

    [Fact]
    public async Task Fifo_layers_and_consumptions_survive_atomic_commit_and_reopen()
    {
        var layer = new StockLayer { InitialQuantity = 3, RemainingQuantity = 3, InitialValue = 100, RemainingValue = 100 };
        var variant = new ProductVariant { Quantity = 3, StockLayerVersion = 1, Layers = [layer] };
        var product = new Product { Name = "FIFO", Variants = [variant] };
        var issued = FifoCostCalculator.Consume(variant, 2);
        var sale = new Sale { Items = [new() { Consumptions = issued.ToList() }] };
        await new SqliteInventoryStore(store!).CommitAsync(InventoryCommit.Create(products: [product], sales: [sale]));
        await store!.CloseAsync();

        var actual = (await new SqliteCatalogRepository(store).GetProductAsync(product.Id))!.Variants[0];
        FifoCostCalculator.Validate(actual);
        Assert.Equal(33.33m, FifoCostCalculator.Value(actual).TotalValue);
        Assert.Equal(layer.Id, Assert.Single(actual.Layers).Id);
        Assert.Equal(issued[0], (await new SqliteSalesRepository(store).GetSaleAsync(sale.Id))!.Items[0].Consumptions[0]);
    }

    [Fact]
    public async Task Catalog_starts_empty_and_preserves_nested_product_data()
    {
        var repository = new SqliteCatalogRepository(store!);
        Assert.Empty(await repository.GetProductsAsync());

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
    public async Task Purchase_expenses_and_corrected_receipt_costs_persist_atomically()
    {
        var catalog = new SqliteCatalogRepository(store!);
        var commerce = new SqliteCommerceRepository(store!);
        var product = new Product { Name = "Товар", Sku = "EXP" };
        await catalog.UpsertProductAsync(product);
        var purchase = new Purchase { Number = "EXP", CurrencyCode = "UZS", Items = [new() { ProductId = product.Id, ProductName = product.Name, Quantity = 2, UnitPriceCny = 100 }] };
        var service = new PurchaseReceivingService(catalog, new SqliteInventoryStore(store!), commerce);
        await service.SaveAsync(purchase);
        await service.ReceiveAsync(purchase, [new(purchase.Items[0].Id, 1, 0)]);
        purchase.Expenses.Add(new() { Name = "Доставка", Amount = 2, CurrencyCode = "USD", RateUzs = 12000 });
        await service.SaveAsync(purchase);
        await service.ReceiveAsync(purchase, [new(purchase.Items[0].Id, 1, 0)]);
        await store!.CloseAsync();
        var restored = (await commerce.GetPurchaseAsync(purchase.Id))!;
        Assert.Equal(24200, restored.TotalCostUzs);
        Assert.Single(restored.Expenses);
        Assert.Equal(2, restored.Receipts.Count);
        Assert.Equal(2, (await catalog.GetProductAsync(product.Id))!.Quantity);
        Assert.Equal(12100, (await catalog.GetProductAsync(product.Id))!.CostUzs);
        var history = await new SqlitePurchaseHistoryRepository(store).GetAsync();
        Assert.Equal(2, history.ProductCosts.Count);
        Assert.All(history.ProductCosts, x => Assert.Equal(12100, x.UnitLandedCostUzs));
    }

    [Fact]
    public async Task Deleting_every_product_does_not_restore_data()
    {
        var repository = new SqliteCatalogRepository(store!);
        var initial = CreateProduct();
        await repository.UpsertProductAsync(initial);
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
        var initial = CreateProduct(ProductStatus.LowStock, 2);
        await catalog.UpsertProductAsync(initial);
        var product = (await catalog.GetProductsAsync()).Single(item => item.Id == initial.Id);
        var variant = Assert.Single(product.Variants);
        var service = new StockAdjustmentService(catalog, new SqliteInventoryStore(store!), settings, new ProductStatusService());

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
        var initial = CreateProduct(ProductStatus.InStock, 5);
        await catalog.UpsertProductAsync(initial);
        var product = (await catalog.GetProductsAsync()).Single(item => item.Id == initial.Id);
        var variant = product.Variants.First(item => item.AvailableQuantity >= 1);
        var originalQuantity = variant.Quantity!.Value;
        var sale = new Sale
        {
            Number = "SALE-SQLITE-001",
            Status = SaleStatus.Draft,
            Items = [new SaleItem { ProductId = product.Id, ProductVariantId = variant.Id, ProductName = product.Name, Color = variant.Color, Size = variant.Size, Quantity = 1, UnitPriceUzs = product.SellingPriceUzs }]
        };
        var inventory = new SalesInventoryService(catalog, new SqliteInventoryStore(store!));

        await inventory.ReserveAsync(sale);
        Assert.Equal(SaleStatus.Reserved, sale.Status);
        Assert.Equal(1, (await catalog.GetProductAsync(product.Id))!.Variants.Single(item => item.Id == variant.Id).ReservedQuantity);

        await new SalesPaymentService(new SqliteInventoryStore(store!)).AddAsync(sale, new SalePayment
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

        await new SalesReturnService(catalog, new SqliteInventoryStore(store!)).CreateAsync(sale, new SaleReturn
        {
            Reason = "Не подошёл размер",
            RefundAmountUzs = sale.TotalUzs,
            Items = [new SaleReturnItem { SaleItemId = sale.Items[0].Id, Quantity = 1, Disposition = ReturnDisposition.Restock }]
        });
        Assert.Equal(SaleStatus.Returned, sale.Status);
        Assert.Equal(originalQuantity, (await catalog.GetProductAsync(product.Id))!.Variants.Single(item => item.Id == variant.Id).Quantity);
        Assert.Contains(await movements.GetAsync(), item => item.Type == StockMovementType.Return);

        await new SalesPaymentService(new SqliteInventoryStore(store!)).AddAsync(sale, new SalePayment
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
        var initial = CreateProduct(ProductStatus.InStock, 5);
        await catalog.UpsertProductAsync(initial);
        var product = (await catalog.GetProductsAsync()).Single(item => item.Id == initial.Id);
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
        var service = new PurchaseReceivingService(catalog, new SqliteInventoryStore(store!));

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
    public async Task Inventory_commit_writes_every_collection_once_and_keeps_existing_order()
    {
        var catalog = new SqliteCatalogRepository(store!);
        var first = CreateProduct(ProductStatus.InStock, 5);
        var second = CreateProduct(ProductStatus.InStock, 6);
        await catalog.UpsertProductAsync(first);
        await catalog.UpsertProductAsync(second);
        var third = CreateProduct(ProductStatus.InStock, 7);
        second.Name = "Изменённое имя";
        var sale = new Sale { Number = "SALE-COMMIT", Status = SaleStatus.Reserved };
        var purchase = new Purchase { Number = "PO-COMMIT", Status = PurchaseStatus.Received };
        var movement = new StockMovement { ProductName = "Test", QuantityDelta = 2 };
        var cost = new ProductCostHistoryEntry { Id = Guid.NewGuid(), ProductName = "Test" };
        var rate = new ExchangeRateHistoryEntry { Id = Guid.NewGuid(), RateUzs = 1_800 };
        var changes = 0;
        store!.BusinessDataChanged += (_, _) => changes++;
        var inventory = new SqliteInventoryStore(store);

        await inventory.CommitAsync(InventoryCommit.Create(products: [second, third], sales: [sale], purchases: [purchase], movements: [movement], productCosts: [cost], exchangeRate: rate));

        Assert.Equal(1, changes);
        var products = await catalog.GetProductsAsync();
        Assert.Equal([first.Id, second.Id, third.Id], products.Select(item => item.Id).ToArray());
        Assert.Equal("Изменённое имя", products[1].Name);
        Assert.Equal("SALE-COMMIT", Assert.Single(await new SqliteSalesRepository(store).GetSalesAsync()).Number);
        Assert.Equal("PO-COMMIT", Assert.Single(await new SqliteCommerceRepository(store).GetPurchasesAsync()).Number);
        Assert.Single(await new SqliteStockMovementRepository(store).GetAsync());
        var history = await new SqlitePurchaseHistoryRepository(store).GetAsync();
        Assert.Single(history.ProductCosts);
        Assert.Single(history.ExchangeRates);
    }

    [Fact]
    public async Task Inventory_commit_is_idempotent_for_immutable_facts()
    {
        var inventory = new SqliteInventoryStore(store!);
        var movement = new StockMovement { ProductName = "Test", QuantityDelta = 2 };
        var cost = new ProductCostHistoryEntry { Id = Guid.NewGuid(), ProductName = "Test", UnitPriceCny = 10 };
        var rate = new ExchangeRateHistoryEntry { Id = Guid.NewGuid(), RateUzs = 1_800 };
        await inventory.CommitAsync(InventoryCommit.Create(movements: [movement], productCosts: [cost], exchangeRate: rate));
        movement.QuantityDelta = 99;
        cost.UnitPriceCny = 99;

        await inventory.CommitAsync(InventoryCommit.Create(movements: [movement, new StockMovement { ProductName = "Next", QuantityDelta = 1 }], productCosts: [cost], exchangeRate: rate));

        var movements = await new SqliteStockMovementRepository(store!).GetAsync();
        Assert.Equal(2, movements.Count);
        Assert.Equal(2, movements[0].QuantityDelta);
        var history = await new SqlitePurchaseHistoryRepository(store!).GetAsync();
        Assert.Equal(10, Assert.Single(history.ProductCosts).UnitPriceCny);
        Assert.Single(history.ExchangeRates);
    }

    [Fact]
    public async Task Inventory_commit_is_all_or_nothing()
    {
        var catalog = new SqliteCatalogRepository(store!);
        var existing = CreateProduct(ProductStatus.InStock, 5);
        await catalog.UpsertProductAsync(existing);
        var changes = 0;
        store!.BusinessDataChanged += (_, _) => changes++;
        existing.Name = "Не должно сохраниться";

        // Строка без данных нарушает NOT NULL во второй операции пакета.
        var broken = new AggregateOperation("stock.movements", Guid.NewGuid(), AggregateWriteMode.Upsert, null);
        await Assert.ThrowsAnyAsync<Exception>(() => store.ApplyAsync([AggregateOperation.Upsert("catalog.products", existing.Id, existing), broken]));

        Assert.Equal(0, changes);
        Assert.NotEqual("Не должно сохраниться", (await catalog.GetProductAsync(existing.Id))!.Name);
        Assert.Empty(await new SqliteStockMovementRepository(store).GetAsync());
    }

    [Fact]
    public async Task Repository_upsert_touches_only_its_own_row_and_keeps_position()
    {
        var catalog = new SqliteCatalogRepository(store!);
        var first = CreateProduct(ProductStatus.InStock, 1);
        var second = CreateProduct(ProductStatus.InStock, 2);
        var third = CreateProduct(ProductStatus.InStock, 3);
        await catalog.UpsertProductAsync(first);
        await catalog.UpsertProductAsync(second);
        await catalog.UpsertProductAsync(third);
        var connection = new SQLiteAsyncConnection(DatabasePath);
        try
        {
            Task<long> Ticks(Product product) => connection.ExecuteScalarAsync<long>("SELECT UpdatedAtUtcTicks FROM aggregate_records WHERE key = ?", $"catalog.products:{product.Id:N}");
            var firstBefore = await Ticks(first);
            var thirdBefore = await Ticks(third);
            await Task.Delay(20);

            second.Name = "Обновлён";
            await catalog.UpsertProductAsync(second);

            Assert.Equal(firstBefore, await Ticks(first));
            Assert.Equal(thirdBefore, await Ticks(third));
            var products = await catalog.GetProductsAsync();
            Assert.Equal([first.Id, second.Id, third.Id], products.Select(item => item.Id).ToArray());
            Assert.Equal("Обновлён", products[1].Name);

            await catalog.DeleteProductAsync(first.Id);
            var fourth = CreateProduct(ProductStatus.InStock, 4);
            await catalog.UpsertProductAsync(fourth);
            Assert.Equal([second.Id, third.Id, fourth.Id], (await catalog.GetProductsAsync()).Select(item => item.Id).ToArray());
            Assert.Equal(thirdBefore, await Ticks(third));
        }
        finally { await connection.CloseAsync(); }
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

    [Fact]
    public async Task Google_snapshot_replaces_all_authoritative_collections_in_one_sqlite_transaction()
    {
        var existingCatalog = new SqliteCatalogRepository(store!);
        _ = await existingCatalog.GetProductsAsync();
        var product = new Product { Sku = "SYNC-1", Name = "Облачный товар", Status = ProductStatus.InStock };
        var customer = new Customer { Name = "Облачный клиент" };
        var sale = new Sale { Number = "SYNC-SALE-1", CustomerId = customer.Id, CustomerName = customer.Name };
        var post = new ContentPost { Title = "Облачный контент", Status = ContentStatus.Ready };
        var snapshot = new DonaSyncSnapshot
        {
            Products = [product],
            Customers = [customer],
            Sales = [sale],
            Marketing = new MarketingData { ContentPosts = [post] },
            BusinessSettings = new BusinessSettings { SaleNumberPrefix = "CLOUD", LowStockThreshold = 9 }
        };

        await store!.ReplaceSnapshotAsync(snapshot);
        await store.CloseAsync();

        Assert.Equal(product.Id, Assert.Single(await new SqliteCatalogRepository(store).GetProductsAsync()).Id);
        Assert.Equal(customer.Id, Assert.Single(await new SqliteSalesRepository(store).GetCustomersAsync()).Id);
        Assert.Equal(sale.Id, Assert.Single(await new SqliteSalesRepository(store).GetSalesAsync()).Id);
        Assert.Equal(post.Id, Assert.Single(await new SqliteMarketingRepository(store).GetContentPostsAsync()).Id);
        Assert.Equal("CLOUD", (await new SqliteBusinessSettingsRepository(store).GetAsync()).SaleNumberPrefix);
        Assert.Empty(await new SqliteCommerceRepository(store).GetPurchasesAsync());
    }

    [Fact]
    public async Task Replacing_snapshot_does_not_raise_business_data_changed()
    {
        var changes = 0;
        store!.BusinessDataChanged += (_, _) => changes++;

        await store.ReplaceSnapshotAsync(new DonaSyncSnapshot
        {
            Products = [new Product { Sku = "PULL-1", Name = "Скачанный товар", Status = ProductStatus.InStock }]
        });

        Assert.Equal(0, changes);
    }

    private static Product CreateProduct(ProductStatus status = ProductStatus.InStock, int quantity = 5) => new()
    {
        Sku = "TEST-001",
        Name = "Тестовый товар",
        Status = status,
        SellingPriceUzs = 100_000,
        Variants = [new ProductVariant { Color = "Чёрный", Size = "M", Quantity = quantity, StockLayerVersion = 1,
            Layers = quantity == 0 ? [] : [new() { Source = StockLayerSource.OpeningBalance, InitialQuantity = quantity, RemainingQuantity = quantity, InitialValue = 0, RemainingValue = 0 }] }]
    };
}
