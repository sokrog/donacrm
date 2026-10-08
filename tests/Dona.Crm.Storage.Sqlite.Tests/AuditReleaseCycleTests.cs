using System.Text.Json;
using Dona.Crm.Storage.Sqlite;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;

namespace Dona.Crm.Storage.Sqlite.Tests;

public sealed partial class SqliteRepositoryTests
{
    private static SqliteSyncStore SnapshotStore(SqliteAggregateStore database) => new(database,
        new(database), new(database), new(database), new(database), new(database), new(database), new(database));

    [Fact]
    public async Task Stale_receipt_cannot_overwrite_a_new_cost_revision()
    {
        var catalog = new SqliteCatalogRepository(store!);
        var commerce = new SqliteCommerceRepository(store!);
        var receiving = new PurchaseReceivingService(catalog, new SqliteInventoryStore(store!), commerce);
        var product = new Product { Name = "Товар", Variants = [new()] };
        await catalog.UpsertProductAsync(product);
        var purchase = new Purchase { Number = "STALE-COST", CurrencyCode = "UZS",
            Items = [new() { ProductId = product.Id, ProductVariantId = product.Variants[0].Id, ProductName = product.Name, Quantity = 2, UnitPrice = 100 }] };
        await receiving.SaveAsync(purchase);
        var stale = (await commerce.GetPurchaseAsync(purchase.Id))!;
        purchase.Expenses.Add(new() { Name = "Доставка", Amount = 50 });
        await receiving.SaveAsync(purchase);
        var before = DonaSyncFingerprint.Create(await SnapshotStore(store!).ReadAsync());
        await Assert.ThrowsAsync<InventoryException>(() => receiving.ReceiveAsync(stale, [new(stale.Items[0].Id, 1, 0)]));
        Assert.Equal(before, DonaSyncFingerprint.Create(await SnapshotStore(store!).ReadAsync()));
    }

    [Fact]
    public async Task Audit_cycle_tariff_partial_receipt_defect_sale_return_late_cost_zip_and_cloud_snapshot()
    {
        var catalog = new SqliteCatalogRepository(store!);
        var commerce = new SqliteCommerceRepository(store!);
        var sales = new SqliteSalesRepository(store!);
        var inventoryStore = new SqliteInventoryStore(store!);
        var receiving = new PurchaseReceivingService(catalog, inventoryStore, commerce);
        var inventory = new SalesInventoryService(catalog, inventoryStore, sales: sales);
        var payments = new SalesPaymentService(inventoryStore, sales);
        var partner = new Intermediary { Name = "Тестовый посредник", CommissionPercent = 10, RatePerKgUsd = 10, MinimumWeightKg = 3 };
        await commerce.UpsertIntermediaryAsync(partner);
        var supplier = new Supplier { Name = "Тестовый поставщик", DefaultIntermediaryId = partner.Id, Moq = 100 };
        await commerce.UpsertSupplierAsync(supplier);
        var product = new Product { Name = "Тестовый товар", PreferredSupplierId = supplier.Id, Variants = [new()] };
        await catalog.UpsertProductAsync(product);
        var purchase = new Purchase { Number = "AUDIT-RELEASE", CurrencyCode = "UZS", IntermediaryId = partner.Id,
            SupplierId = supplier.Id, Status = PurchaseStatus.Ordered,
            Items = [new() { ProductId = product.Id, ProductVariantId = product.Variants[0].Id, ProductName = product.Name,
                Quantity = 4, UnitPrice = 100, UnitWeightKg = .5m }] };
        Assert.Equal(2, PurchaseTariffCalculator.Apply(purchase, partner, 10));
        await receiving.SaveAsync(purchase);
        Assert.Equal(740, purchase.TotalCostUzs);
        var receiptId = Guid.NewGuid();
        await receiving.ReceiveAsync(purchase, [new(purchase.Items[0].Id, 3, 1)], receiptId);
        await receiving.ReceiveAsync(purchase, [new(purchase.Items[0].Id, 3, 1)], receiptId);
        Assert.Equal(PurchaseStatus.PartiallyReceived, purchase.Status);
        Assert.Equal(2, (await catalog.GetProductAsync(product.Id))!.Quantity);
        Assert.Equal(1, PurchaseAvailability.Expected(product.Id, [purchase]));

        var sale = new Sale { Number = "AUDIT-SALE", DiscountMode = SaleDiscountMode.Percent, DiscountPercent = 10, DeliveryChargeUzs = 50,
            Items = [new() { ProductId = product.Id, ProductVariantId = product.Variants[0].Id, ProductName = product.Name, Quantity = 2, UnitPriceUzs = 1000 }] };
        await inventory.ReserveAsync(sale);
        await payments.AddAsync(sale, new() { Type = PaymentOperationType.Payment, Status = PaymentStatus.Completed, Method = PaymentMethod.Cash, AmountUzs = 1850 });
        Assert.Equal(SaleStatus.Reserved, sale.Status);
        await inventory.MarkShippedAsync(sale);
        var originalCosts = JsonSerializer.Serialize(sale.Items[0].Consumptions);
        await new SalesReturnService(catalog, inventoryStore, sales).CreateAsync(sale, new() { Reason = "Размер",
            Items = [new() { SaleItemId = sale.Items[0].Id, Quantity = 1, Disposition = ReturnDisposition.Restock }] });
        Assert.Equal(900, sale.RefundDueUzs);
        await payments.AddAsync(sale, new() { Type = PaymentOperationType.Refund, Status = PaymentStatus.Completed, Method = PaymentMethod.Cash, AmountUzs = 900 });
        await inventory.CompleteAsync(sale);
        await inventory.CompleteAsync(sale);
        Assert.Equal(1, (await catalog.GetProductAsync(product.Id))!.Quantity);
        Assert.Equal(950, sale.NetTotalUzs);
        Assert.Equal(0, sale.BalanceDueUzs);
        Assert.Equal(0, sale.RefundDueUzs);

        purchase.Expenses.Add(new() { Name = "Поздний расход", Amount = 60 });
        await receiving.SaveAsync(purchase);
        await receiving.ReceiveAsync(purchase, [new(purchase.Items[0].Id, 1, 0)]);
        purchase.IsCostFinalized = true;
        await receiving.SaveAsync(purchase);
        Assert.Equal(0, PurchaseTariffCalculator.Apply(purchase, partner, 10));
        Assert.Equal(800, purchase.TotalCostUzs);
        Assert.Equal(PurchaseStatus.Received, purchase.Status);
        Assert.Equal(0, PurchaseAvailability.Expected(product.Id, [purchase]));
        Assert.Equal(originalCosts, JsonSerializer.Serialize((await sales.GetSaleAsync(sale.Id))!.Items[0].Consumptions));

        var source = SnapshotStore(store!);
        var snapshot = await source.ReadAsync();
        Assert.Equal(2, Assert.Single(snapshot.Products).Quantity);
        Assert.Equal(500, FifoCostCalculator.Value(snapshot.Products[0].Variants[0]).TotalValue);
        var report = PurchaseLayerReport.Build(purchase, snapshot.Products, snapshot.Sales, snapshot.StockMovements);
        Assert.All(report.Layers, row => { Assert.Equal(0, row.QuantityDifference); Assert.Equal(0, row.ValueDifference); });
        var zip = BackupArchiveCodec.Create(BackupSnapshotMapper.FromSyncSnapshot(snapshot));
        var images = new FileLocalImageStore(Path.Combine(directory, "images"));
        await using var destination = new SqliteAggregateStore(new(Path.Combine(directory, "restored.db3")));
        var target = SnapshotStore(destination);
        var restore = new BackupRestoreService(target, images);
        var preview = await restore.PreviewAsync(zip);
        Assert.Equal(1, preview.Purchases);
        Assert.Equal(1, preview.Sales);
        var restored = await restore.RestoreAsync(zip);
        Assert.Empty(BackupArchiveCodec.Inspect(restored.AutomaticBackup.Content).Snapshot.Products);
        await destination.CloseAsync();
        Assert.Equal(DonaSyncFingerprint.Create(snapshot), DonaSyncFingerprint.Create(await target.ReadAsync()));

        // The same serialized snapshot contract is used by Drive, without making an external request.
        var cloudSnapshot = JsonSerializer.Deserialize<DonaSyncSnapshot>(JsonSerializer.Serialize(snapshot))!;
        await target.ReplaceAsync(cloudSnapshot);
        Assert.Equal(DonaSyncFingerprint.Create(snapshot), DonaSyncFingerprint.Create(await target.ReadAsync()));
        cloudSnapshot.SchemaVersion = 2;
        await Assert.ThrowsAsync<InvalidDataException>(() => target.ReplaceAsync(cloudSnapshot));
        Assert.Equal(DonaSyncFingerprint.Create(snapshot), DonaSyncFingerprint.Create(await target.ReadAsync()));
    }
}
