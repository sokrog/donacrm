using Dona.Crm.Storage.Browser;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;
using Dona.Crm.Web.Storage;
using Microsoft.JSInterop;

namespace Dona.Crm.Storage.Browser.Tests;

public sealed partial class BrowserCrmRepositoryTests
{
    [Fact]
    public async Task Image_transfer_preserves_current_business_data_and_updates_all_image_owners()
    {
        var repository = new BrowserCrmRepository(new KeyValueJsRuntime());
        ProductImage Image() => new() { Storage = ProductImageStorage.Local, Url = "local:a.png", StorageKey = "a.png" };
        var product = new Product { Name = "Товар", Images = [Image()], ImageUrl = "local:a.png" };
        await repository.ReplaceSnapshotAsync(new() { Products = [product], Marketing = new() { Collections = [new() { Images = [Image()] }], Outfits = [new() { Images = [Image()] }] } });
        product.Name = "Изменено во время загрузки";
        await repository.UpsertProductAsync(product);
        await repository.ApplyUploadedImageAsync("local:a.png", new("target", "photo", "image/png", 3));
        var snapshot = await repository.ReadSnapshotAsync();
        Assert.Equal(product.Name, snapshot.Products.Single().Name);
        Assert.Equal("drive:target", snapshot.Products.Single().ImageUrl);
        Assert.All(LocalImageKey.EnumerateImages(snapshot), image => Assert.Equal("target", image.StorageKey));
    }

    [Fact]
    public async Task Tariff_receipt_zip_restores_into_fresh_browser_store_without_duplicate_expenses()
    {
        var source = new BrowserCrmRepository(new KeyValueJsRuntime());
        var partner = new Intermediary { Name = "Посредник", CommissionPercent = 5, RatePerKgUsd = 2, MinimumWeightKg = 3 };
        await source.UpsertIntermediaryAsync(partner);
        var product = new Product { Name = "Товар", Variants = [new()] };
        await source.UpsertProductAsync(product);
        var purchase = new Purchase { Number = "BROWSER-ZIP", CurrencyCode = "USD", RateToUzs = 12000, IntermediaryId = partner.Id,
            Items = [new() { ProductId = product.Id, ProductVariantId = product.Variants[0].Id, ProductName = product.Name,
                Quantity = 2, UnitPrice = 10, UnitWeightKg = .5m }] };
        PurchaseTariffCalculator.Apply(purchase, partner);
        var receiving = new PurchaseReceivingService(source, source, source);
        await receiving.SaveAsync(purchase);
        await receiving.ReceiveAsync(purchase, [new(purchase.Items[0].Id, 1, 0)]);
        var snapshot = await source.ReadSnapshotAsync();
        var bytes = BackupArchiveCodec.Create(BackupSnapshotMapper.FromSyncSnapshot(snapshot));
        var targetJs = new KeyValueJsRuntime();
        var target = new BrowserCrmRepository(targetJs);
        var restore = new BackupRestoreService(target, new BrowserLocalImageStore(targetJs));
        Assert.Equal(1, (await restore.PreviewAsync(bytes)).NewPurchases);
        await restore.RestoreAsync(bytes);
        target = new BrowserCrmRepository(targetJs);
        Assert.Equal(DonaSyncFingerprint.Create(snapshot), DonaSyncFingerprint.Create(await target.ReadSnapshotAsync()));
        var restored = (await target.GetPurchaseAsync(purchase.Id))!;
        Assert.Equal(0, PurchaseTariffCalculator.Apply(restored, partner));
        Assert.Equal(2, restored.Expenses.Count);
        Assert.Equal(1, (await target.GetProductAsync(product.Id))!.Quantity);
        Assert.Equal(162000, FifoCostCalculator.Value((await target.GetProductAsync(product.Id))!.Variants[0]).TotalValue);
        var invalid = await target.ReadSnapshotAsync();
        invalid.SchemaVersion = 2;
        await Assert.ThrowsAsync<InvalidDataException>(() => target.ReplaceSnapshotAsync(invalid));
        Assert.Equal(DonaSyncFingerprint.Create(snapshot), DonaSyncFingerprint.Create(await new BrowserCrmRepository(targetJs).ReadSnapshotAsync()));
    }

    [Fact]
    public async Task Fifo_layers_and_consumptions_survive_atomic_commit_and_reload()
    {
        var js = new KeyValueJsRuntime();
        var repository = new BrowserCrmRepository(js);
        var layer = new StockLayer { InitialQuantity = 3, RemainingQuantity = 3, InitialValue = 100, RemainingValue = 100 };
        var variant = new ProductVariant { Quantity = 3, StockLayerVersion = 1, Layers = [layer] };
        var product = new Product { Name = "FIFO", Variants = [variant] };
        var issued = FifoCostCalculator.Consume(variant, 2);
        var sale = new Sale { Items = [new() { Consumptions = issued.ToList() }] };
        await repository.CommitAsync(InventoryCommit.Create(products: [product], sales: [sale]));

        var reopened = new BrowserCrmRepository(js);
        var actual = (await reopened.GetProductAsync(product.Id))!.Variants[0];
        FifoCostCalculator.Validate(actual);
        Assert.Equal(33.33m, FifoCostCalculator.Value(actual).TotalValue);
        Assert.Equal(layer.Id, Assert.Single(actual.Layers).Id);
        Assert.Equal(issued[0], (await reopened.GetSaleAsync(sale.Id))!.Items[0].Consumptions[0]);
    }

    [Fact]
    public async Task Late_expense_updates_costs_without_receiving_twice_and_survives_reload()
    {
        var js = new KeyValueJsRuntime();
        var repository = new BrowserCrmRepository(js);
        var product = new Product { Sku = "COST", Name = "Товар", Variants = [new()] };
        await repository.UpsertProductAsync(product);
        var item = new PurchaseItem { ProductId = product.Id, ProductVariantId = product.Variants[0].Id, ProductName = product.Name, Quantity = 2, UnitPrice = 100 };
        var purchase = new Purchase { Number = "COST", CurrencyCode = "UZS", Items = [item] };
        var service = new PurchaseReceivingService(repository, repository, repository);
        await service.SaveAsync(purchase);
        await service.ReceiveAsync(purchase, [new(item.Id, 2, 0)]);
        var sale = new Sale { Number = "OLD", Items = [new() { ProductId = product.Id, Quantity = 1, UnitCostUzs = 100 }] };
        await repository.UpsertSaleAsync(sale);

        purchase.Expenses.Add(new() { Name = "Доставка", Amount = 60 });
        purchase.IsCostFinalized = true;
        await service.SaveAsync(purchase);
        await service.SaveAsync(purchase);

        var reloaded = new BrowserCrmRepository(js);
        var restored = (await reloaded.GetPurchaseAsync(purchase.Id))!;
        Assert.Equal(260, restored.TotalCostUzs);
        Assert.True(restored.IsCostFinalized);
        Assert.Equal(2, restored.CostRevisions.Count);
        var actual = (await reloaded.GetProductAsync(product.Id))!;
        Assert.Equal(2, actual.Quantity);
        Assert.Equal(130, FifoCostCalculator.Value(actual.Variants[0]).KnownValue / actual.Quantity);
        var history = await ((IPurchaseHistoryRepository)reloaded).GetAsync();
        Assert.Equal(130, Assert.Single(history.ProductCosts).UnitLandedCostUzs);
        Assert.Single(await ((IStockMovementRepository)reloaded).GetAsync());
        Assert.Equal(100, (await reloaded.GetSaleAsync(sale.Id))!.Items[0].UnitCostUzs);
    }

    [Fact]
    public async Task Failed_cost_save_is_atomic_and_old_purchase_does_not_override_new_purchase_price()
    {
        var js = new KeyValueJsRuntime();
        var repository = new BrowserCrmRepository(js);
        var product = new Product { Sku = "T", Name = "Товар", Variants = [new()] };
        await repository.UpsertProductAsync(product);
        var service = new PurchaseReceivingService(repository, repository, repository);
        var old = new Purchase { Number = "OLD", CurrencyCode = "UZS", Items = [new() { ProductId = product.Id, ProductVariantId = product.Variants[0].Id, ProductName = product.Name, Quantity = 1, UnitPrice = 100 }] };
        await service.SaveAsync(old);
        await service.ReceiveAsync(old, [new(old.Items[0].Id, 1, 0)]);
        old.Expenses.Add(new() { Name = "Поздняя доставка", Amount = 50 });
        js.QuotaExceeded = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(old));
        js.QuotaExceeded = false;
        Assert.Single(old.CostRevisions);
        Assert.Empty((await repository.GetPurchaseAsync(old.Id))!.Expenses);
        Assert.Equal(100, (await repository.GetProductAsync(product.Id))!.Variants[0].Layers[0].RemainingValue);
        Assert.Equal(100, Assert.Single((await ((IPurchaseHistoryRepository)repository).GetAsync()).ProductCosts).UnitLandedCostUzs);

        var newer = new Purchase { Number = "NEW", CurrencyCode = "UZS", Items = [new() { ProductId = product.Id, ProductVariantId = product.Variants[0].Id, ProductName = product.Name, Quantity = 1, UnitPrice = 200 }] };
        await service.SaveAsync(newer);
        await service.ReceiveAsync(newer, [new(newer.Items[0].Id, 1, 0)]);
        await service.SaveAsync(old);
        Assert.Equal(350, FifoCostCalculator.Value((await repository.GetProductAsync(product.Id))!.Variants[0]).KnownValue);
        Assert.Equal(150, (await ((IPurchaseHistoryRepository)repository).GetAsync()).ProductCosts.Single(x => x.PurchaseId == old.Id).UnitLandedCostUzs);
    }

    [Fact]
    public async Task Snapshot_survives_a_new_repository_instance()
    {
        var javascript = new KeyValueJsRuntime();
        ICatalogRepository first = new BrowserCrmRepository(javascript);
        var product = new Product
        {
            Sku = "WEB-1",
            Name = "Browser dress",
            Variants = [new ProductVariant { Color = "Black", Size = "M", Quantity = 3 }]
        };

        await first.UpsertProductAsync(product);
        ICatalogRepository restored = new BrowserCrmRepository(javascript);

        var value = Assert.Single(await restored.GetProductsAsync());
        Assert.Equal("WEB-1", value.Sku);
        Assert.Equal(3, Assert.Single(value.Variants).Quantity);
    }

    [Fact]
    public async Task First_start_seeds_categories_without_demo_transactions()
    {
        var repository = new BrowserCrmRepository(new KeyValueJsRuntime());

        var categories = await ((ICommerceRepository)repository).GetCategoriesAsync();

        Assert.NotEmpty(categories);
        Assert.Empty(await ((ISalesRepository)repository).GetSalesAsync());
        Assert.Empty(await repository.GetProductsAsync());
    }

    [Fact]
    public async Task Quota_error_is_reported_as_readable_InvalidOperationException()
    {
        var javascript = new KeyValueJsRuntime();
        var repository = new BrowserCrmRepository(javascript);
        await repository.GetProductsAsync();
        javascript.QuotaExceeded = true;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ((ICatalogRepository)repository).UpsertProductAsync(new Product { Sku = "BIG", Name = "Большое фото" }));

        Assert.Contains("Хранилище браузера заполнено", error.Message);
    }

    [Fact]
    public async Task Sync_operations_and_checkpoint_persist_through_the_key_value_store()
    {
        var javascript = new KeyValueJsRuntime();
        var checkpoints = new BrowserGoogleSyncCheckpointStore(javascript);

        await checkpoints.WriteAsync(new GoogleSyncCheckpoint { LocalVersion = "v1", GoogleVersion = "g1" });
        var restored = await new BrowserGoogleSyncCheckpointStore(javascript).ReadAsync();

        Assert.Equal("v1", restored?.LocalVersion);
        Assert.Equal("g1", restored?.GoogleVersion);
        Assert.Contains(javascript.Keys, key => key.Contains("sync-checkpoint"));
    }

    [Fact]
    public async Task Incomplete_draft_can_be_deleted_and_remains_deleted_after_backup_restore()
    {
        var repository = new BrowserCrmRepository(new KeyValueJsRuntime());
        var draft = new Sale { Number = SaleNumberGenerator.Generate([], "SALE", DateTimeOffset.Now), Status = SaleStatus.Draft, Items = [new()] };
        await repository.UpsertSaleAsync(draft);
        await new SalesInventoryService(repository, repository, sales: repository).DeleteAsync(draft.Id);
        Assert.Empty(await repository.GetSalesAsync());
        Assert.NotNull((await repository.GetSaleAsync(draft.Id))!.DeletedAt);
        Assert.Equal(draft.Number, Assert.Single(await repository.GetAllSalesAsync()).Number);
        Assert.EndsWith("-002", SaleNumberGenerator.Generate(await repository.GetAllSalesAsync(), "SALE", DateTimeOffset.Now));
        Assert.Empty((await repository.ReadSnapshotAsync()).StockMovements);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpsertSaleAsync(draft));
        var backup = BackupArchiveCodec.Create(BackupSnapshotMapper.FromSyncSnapshot(await repository.ReadSnapshotAsync()));
        var restored = new BrowserCrmRepository(new KeyValueJsRuntime());
        await restored.ReplaceSnapshotAsync(BackupSnapshotMapper.ToSyncSnapshot(BackupArchiveCodec.Inspect(backup).Snapshot));
        Assert.Empty(await restored.GetSalesAsync());
        Assert.NotNull((await restored.GetSaleAsync(draft.Id))!.DeletedAt);
    }

    [Fact]
    public async Task Deleting_reserved_sale_releases_only_its_own_reservation()
    {
        var (repository, service, product, sale) = await DeletionScenarioAsync();
        var other = new Sale { Number = "OTHER", Status = SaleStatus.Draft, Items = [new() { ProductId = product.Id,
            ProductVariantId = product.Variants[0].Id, Quantity = 2, UnitPriceUzs = 20 }] };
        await service.ReserveAsync(sale);
        await service.ReserveAsync(other);
        await service.DeleteAsync(sale.Id);
        var variant = (await repository.GetProductAsync(product.Id))!.Variants[0];
        Assert.Equal(10, variant.Quantity);
        Assert.Equal(2, variant.ReservedQuantity);
        Assert.Single(await repository.GetSalesAsync());
        Assert.Equal(-3, (await repository.ReadSnapshotAsync()).StockMovements.Last().ReservedDelta);
    }

    [Theory]
    [InlineData(ReturnDisposition.Restock, 1, 10, false)]
    [InlineData(ReturnDisposition.Defect, 1, 9, false)]
    [InlineData(ReturnDisposition.Restock, 3, 10, false)]
    [InlineData(ReturnDisposition.Defect, 3, 7, false)]
    [InlineData(ReturnDisposition.Restock, 1, 10, true)]
    [InlineData(ReturnDisposition.Defect, 1, 9, true)]
    [InlineData(ReturnDisposition.Restock, 3, 10, true)]
    [InlineData(ReturnDisposition.Defect, 3, 7, true)]
    public async Task Deleting_completed_sale_restores_only_outstanding_goods_and_excludes_money(ReturnDisposition disposition, int returnedQuantity, int expected, bool revalue)
    {
        var (repository, service, product, sale) = await DeletionScenarioAsync();
        await service.ReserveAsync(sale);
        await service.MarkShippedAsync(sale);
        await service.CompleteAsync(sale);
        await new SalesPaymentService(repository, repository).AddAsync(sale, new() { Type = PaymentOperationType.Payment,
            Status = PaymentStatus.Completed, Method = PaymentMethod.Cash, AmountUzs = 60 });
        await new SalesReturnService(repository, repository, repository).CreateAsync(sale, new() { Reason = "Частичный возврат",
            Items = [new() { SaleItemId = sale.Items[0].Id, Quantity = returnedQuantity, Disposition = disposition }] });
        var unitCost = revalue ? 15m : 10m;
        if (revalue)
        {
            var currentProduct = (await repository.GetProductAsync(product.Id))!;
            var layer = currentProduct.Variants[0].Layers[0];
            layer.InitialValue = 150;
            layer.RemainingValue = layer.RemainingQuantity * unitCost;
            layer.ValuationRevision++;
            currentProduct.StockValuations.Add(new(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
                StockValuationReason.Revaluation, layer.Id, null, 0, layer.ValuationRevision,
                100, 150, layer.RemainingQuantity * 5m, (10 - layer.RemainingQuantity) * 5m));
            await repository.UpsertProductAsync(currentProduct);
        }
        await service.DeleteAsync(sale.Id);
        var variant = (await repository.GetProductAsync(product.Id))!.Variants[0];
        Assert.Equal(expected, variant.Quantity);
        Assert.Equal(expected * unitCost, variant.Layers.Sum(x => x.RemainingValue));
        Assert.Empty(await repository.GetSalesAsync());
        var deleted = (await repository.GetSaleAsync(sale.Id))!;
        Assert.Empty(SaleFinancialEvents.Build([deleted]));
        Assert.Single(deleted.Payments);
        var snapshot = await repository.ReadSnapshotAsync();
        var analytics = new AnalyticsService().Build([deleted], snapshot.Products, null, null, movements: snapshot.StockMovements);
        Assert.Equal(0, analytics.Orders);
        Assert.Equal(0, analytics.RevenueUzs);
        Assert.Equal((10 - expected) * unitCost, analytics.PeriodExpensesUzs);
        var profit = new ProfitAnalyticsService().Build([deleted], snapshot.Products, [], new MarketingData(), null, null, snapshot.StockMovements);
        Assert.Equal((10 - expected) * unitCost, profit.Suppliers.Sum(x => x.CostUzs));
        var purchase = new Purchase();
        variant.Layers[0].PurchaseId = purchase.Id;
        var reportProduct = (await repository.GetProductAsync(product.Id))!;
        reportProduct.Variants[0] = variant;
        var report = Assert.Single(PurchaseLayerReport.Build(purchase, [reportProduct], [deleted], snapshot.StockMovements).Layers);
        Assert.Equal(0, report.QuantityDifference);
        Assert.Equal(0m, report.ValueDifference);
        Assert.Equal(10 - expected, report.WrittenOff);
        await service.DeleteAsync(sale.Id);
        Assert.Equal(snapshot.StockMovements.Count, (await repository.ReadSnapshotAsync()).StockMovements.Count);
        await Assert.ThrowsAsync<InventoryException>(() => service.MarkShippedAsync(sale));
    }

    [Fact]
    public async Task Failed_deletion_does_not_hide_sale_or_release_any_stock()
    {
        var (repository, service, product, sale) = await DeletionScenarioAsync();
        await service.ReserveAsync(sale);
        var broken = (await repository.GetSaleAsync(sale.Id))!;
        broken.Items.Add(new() { ProductId = Guid.NewGuid(), ProductVariantId = Guid.NewGuid(), ReservedQuantity = 1 });
        await repository.UpsertSaleAsync(broken);
        await Assert.ThrowsAsync<InventoryException>(() => service.DeleteAsync(sale.Id));
        Assert.Null((await repository.GetSaleAsync(sale.Id))!.DeletedAt);
        Assert.Equal(3, (await repository.GetProductAsync(product.Id))!.Variants[0].ReservedQuantity);
        Assert.DoesNotContain((await repository.ReadSnapshotAsync()).StockMovements, x => x.SourceType == "SaleDeletion");
    }

    [Fact]
    public async Task Storage_failure_during_deletion_is_atomic_and_retry_restores_stock_once()
    {
        var js = new KeyValueJsRuntime();
        var repository = new BrowserCrmRepository(js);
        var product = new Product { Name = "Товар", Sku = "FAIL-DELETE", Variants = [new()] };
        StockLayerOperations.Add(product.Variants[0], 5, 50, StockLayerSource.OpeningBalance, DateTimeOffset.UtcNow, "OPENING");
        await repository.UpsertProductAsync(product);
        var sale = new Sale { Number = "FAIL-DELETE", Status = SaleStatus.Draft, Items = [new() { ProductId = product.Id,
            ProductVariantId = product.Variants[0].Id, Quantity = 2, UnitPriceUzs = 20 }] };
        var service = new SalesInventoryService(repository, repository, sales: repository);
        await service.ReserveAsync(sale);
        await service.MarkShippedAsync(sale);
        js.QuotaExceeded = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(sale.Id));
        js.QuotaExceeded = false;
        Assert.Null((await repository.GetSaleAsync(sale.Id))!.DeletedAt);
        Assert.Equal(3, (await repository.GetProductAsync(product.Id))!.Variants[0].Quantity);
        await service.DeleteAsync(sale.Id);
        await service.DeleteAsync(sale.Id);
        Assert.Equal(5, (await repository.GetProductAsync(product.Id))!.Variants[0].Quantity);
    }

    private static async Task<(BrowserCrmRepository Repository, SalesInventoryService Service, Product Product, Sale Sale)> DeletionScenarioAsync()
    {
        var repository = new BrowserCrmRepository(new KeyValueJsRuntime());
        var product = new Product { Name = "Товар", Sku = "DELETE", Variants = [new()] };
        StockLayerOperations.Add(product.Variants[0], 10, 100, StockLayerSource.OpeningBalance, DateTimeOffset.UtcNow, "OPENING");
        await repository.UpsertProductAsync(product);
        var sale = new Sale { Number = "SALE-DELETE", Status = SaleStatus.Draft, Items = [new() { ProductId = product.Id,
            ProductVariantId = product.Variants[0].Id, Quantity = 3, UnitPriceUzs = 20 }] };
        await repository.UpsertSaleAsync(sale);
        return (repository, new SalesInventoryService(repository, repository, sales: repository), product, sale);
    }

    private sealed class KeyValueJsRuntime : IJSRuntime
    {
        private readonly Dictionary<string, string> values = [];

        public bool QuotaExceeded { get; set; }
        public IReadOnlyCollection<string> Keys => values.Keys;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (identifier)
            {
                case "donaStore.get":
                    values.TryGetValue((string)args![0]!, out var value);
                    return ValueTask.FromResult((TValue)(object?)value!);
                case "donaStore.set":
                    if (QuotaExceeded) throw new JSException("Хранилище браузера заполнено. Освободите место или подключите Google Drive для фотографий.");
                    values[(string)args![0]!] = (string)args[1]!;
                    return ValueTask.FromResult(default(TValue)!);
                case "donaStore.remove":
                    values.Remove((string)args![0]!);
                    return ValueTask.FromResult(default(TValue)!);
                case "donaStore.requestPersistence":
                    return ValueTask.FromResult(default(TValue)!);
            }
            throw new NotSupportedException(identifier);
        }
    }
}
