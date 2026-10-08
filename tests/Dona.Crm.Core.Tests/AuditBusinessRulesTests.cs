using System.IO.Compression;
using System.Text.Json;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;

namespace Dona.Crm.Core.Tests;

public sealed class AuditBusinessRulesTests
{
    [Fact]
    public void Catalog_updates_outfit_but_preserves_sale_history_and_marks_missing_products()
    {
        var product = new Product { Name = "Новое название", SellingPriceUzs = 150 };
        var outfit = new Outfit { Products = [new() { ProductId = product.Id, ProductName = "Старое название", SellingPriceUzs = 100 }] };
        var sale = new Sale { Items = [new() { ProductId = product.Id, ProductName = "Старое название", Quantity = 1, UnitPriceUzs = 100 }] };
        Assert.Equal(150, MarketingCatalog.OutfitPrice(outfit, [product]));
        Assert.Equal("Новое название", MarketingCatalog.ProductLabel(product.Id, "Старое название", [product]));
        product.Status = ProductStatus.Archived;
        Assert.Contains("архиве", MarketingCatalog.ProductLabel(product.Id, "Старое название", [product]));
        Assert.Null(MarketingCatalog.OutfitPrice(outfit, []));
        Assert.Contains("Старое название", MarketingCatalog.ProductLabel(product.Id, "Старое название", []));
        Assert.Equal(100, sale.TotalUzs);
        Assert.Equal("Старое название", sale.Items[0].ProductName);
    }

    [Fact]
    public void Order_permission_does_not_create_stock_and_closed_cancelled_orders_are_not_expected()
    {
        var product = new Product { AllowOrderWhenUnavailable = true, Status = ProductStatus.OnOrder };
        var settings = new BusinessSettings { AutoUpdateStockStatus = true, CountReservedAsUnavailable = true };
        var statuses = new ProductStatusService();
        Assert.Equal(ProductStatus.OutOfStock, statuses.Calculate(product, settings));
        product.Variants.Add(new() { Quantity = 2, ReservedQuantity = 2 });
        Assert.Equal(ProductStatus.OutOfStock, statuses.Calculate(product, settings));
        settings.AutoUpdateStockStatus = false;
        Assert.Equal(ProductStatus.OnOrder, statuses.Calculate(product, settings));
        settings.AutoUpdateStockStatus = true;
        product.Status = ProductStatus.Archived;
        Assert.Equal(ProductStatus.Archived, statuses.Calculate(product, settings));
        Purchase Order(PurchaseStatus status, bool closed = false) => new() { Status = status, ClosedAt = closed ? DateTimeOffset.UtcNow : null,
            Items = [new() { ProductId = product.Id, Quantity = 5, ReceivedQuantity = 2, DefectQuantity = 1 }] };
        Assert.Equal(3, PurchaseAvailability.Expected(product.Id,
            [Order(PurchaseStatus.PartiallyReceived), Order(PurchaseStatus.Cancelled), Order(PurchaseStatus.Draft), Order(PurchaseStatus.Ordered, true)]));
    }

    [Fact]
    public void Split_returns_reconcile_discount_rounding_exactly()
    {
        var sale = new Sale { DiscountUzs = 1, Items = [new() { Quantity = 3, UnitPriceUzs = 1 }, new() { Quantity = 3, UnitPriceUzs = 2 }] };
        decimal total = 0;
        foreach (var item in sale.Items)
        for (var i = 0; i < 3; i++)
        {
            total += SaleReturnCalculator.GoodsRefund(sale, new() { Items = [new() { SaleItemId = item.Id, Quantity = 1, Disposition = ReturnDisposition.Restock }] });
            item.ReturnedQuantity++;
        }
        Assert.Equal(sale.TotalUzs, total);
    }

    [Fact]
    public void Neutral_price_names_preserve_source_currency_and_read_old_local_field_names()
    {
        var product = JsonSerializer.Deserialize<Product>("{\"PurchasePriceCny\":12,\"PurchaseCurrencyCode\":\"USD\",\"CnyRateUzs\":12500}")!;
        Assert.Equal(12, product.PlannedPurchasePrice);
        Assert.Equal(150000, product.CostUzs);
        var json = JsonSerializer.Serialize(product);
        Assert.DoesNotContain("PurchasePriceCny", json);
        Assert.DoesNotContain("CnyRateUzs", json);
        var restored = JsonSerializer.Deserialize<Product>(json)!;
        Assert.Equal("USD", restored.PurchaseCurrencyCode);
        Assert.Equal(product.CostUzs, restored.CostUzs);
        var purchase = JsonSerializer.Deserialize<Purchase>("{\"CurrencyCode\":\"USD\",\"CnyRateUzs\":12500,\"Items\":[{\"Quantity\":2,\"UnitPriceCny\":12}]}")!;
        Assert.Equal(300000, purchase.GoodsCostUzs);
        Assert.Equal(purchase.GoodsCostUzs, JsonSerializer.Deserialize<Purchase>(JsonSerializer.Serialize(purchase))!.GoodsCostUzs);
    }

    [Fact]
    public async Task Negative_return_line_rejects_whole_document_without_stock_changes()
    {
        var (sale, catalog, store, inventory) = Setup();
        await inventory.ReserveAsync(sale);
        await inventory.MarkShippedAsync(sale);
        var before = JsonSerializer.Serialize(await catalog.GetProductsAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SalesReturnService(catalog, store).CreateAsync(sale,
            new() { Reason = "Ошибка", Items = [new() { SaleItemId = sale.Items[0].Id, Quantity = 1, Disposition = ReturnDisposition.Restock },
                new() { SaleItemId = Guid.NewGuid(), Quantity = -1, Disposition = ReturnDisposition.Restock }] }));
        Assert.Equal(before, JsonSerializer.Serialize(await catalog.GetProductsAsync()));
        Assert.Empty(sale.Returns);
    }

    [Fact]
    public void Foreign_accounting_currency_is_rejected_on_assignment_and_deserialization()
    {
        var settings = new BusinessSettings();
        Assert.Throws<InvalidOperationException>(() => settings.MainCurrencyCode = "USD");
        Assert.Equal("UZS", settings.MainCurrencyCode);
        Assert.Throws<InvalidOperationException>(() => JsonSerializer.Deserialize<BusinessSettings>("{\"MainCurrencyCode\":\"USD\"}"));
        Assert.Equal("UZS", JsonSerializer.Deserialize<BusinessSettings>(JsonSerializer.Serialize(settings))!.MainCurrencyCode);
    }

    [Fact]
    public void Backup_rejects_foreign_accounting_currency_and_previous_fifo_format()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(zip.CreateEntry("dona-crm-backup.json").Open()))
            writer.Write($"{{\"schemaVersion\":{DonaSyncSnapshot.CurrentSchemaVersion},\"businessSettings\":{{\"mainCurrencyCode\":\"USD\"}}}}");
        Assert.Throws<InvalidOperationException>(() => BackupArchiveCodec.Inspect(stream.ToArray()));
        Assert.Throws<InvalidDataException>(() => new DonaSyncSnapshot { SchemaVersion = 2 }.ValidateFormat());
    }

    private static (Sale Sale, CloningCatalog Catalog, MemoryInventoryStore Store, SalesInventoryService Inventory) Setup()
    {
        var product = new Product { Name = "Товар", Variants = [new() { Quantity = 3 }] };
        var catalog = new CloningCatalog(product);
        var store = new MemoryInventoryStore(catalog);
        var sale = new Sale { Number = "AUDIT", Items = [new() { ProductId = product.Id, ProductVariantId = product.Variants[0].Id,
            ProductName = product.Name, Quantity = 2, UnitPriceUzs = 100 }] };
        return (sale, catalog, store, new SalesInventoryService(catalog, store));
    }

    [Fact]
    public async Task Shipment_consumes_once_and_completion_does_not_touch_inventory_or_require_payment()
    {
        var (sale, catalog, store, inventory) = Setup();
        await inventory.ReserveAsync(sale);
        await Assert.ThrowsAsync<SaleTransitionException>(() => inventory.CompleteAsync(sale));
        await inventory.MarkShippedAsync(sale);
        var stock = JsonSerializer.Serialize(await catalog.GetProductsAsync());
        var movements = store.Movements.Count;
        var costs = JsonSerializer.Serialize(sale.Items[0].Consumptions);
        await inventory.MarkShippedAsync(sale);
        await inventory.CompleteAsync(sale);
        await inventory.CompleteAsync(sale);
        Assert.Equal(stock, JsonSerializer.Serialize(await catalog.GetProductsAsync()));
        Assert.Equal(movements, store.Movements.Count);
        Assert.Equal(costs, JsonSerializer.Serialize(sale.Items[0].Consumptions));
        Assert.Equal(200, sale.BalanceDueUzs);
        Assert.Equal(SaleStatus.Completed, sale.Status);
        Assert.Equal(1, (await catalog.GetProductsAsync())[0].Quantity);
    }

    [Fact]
    public async Task Paid_reservation_cancellation_keeps_refund_due_and_does_not_require_goods_return()
    {
        var (sale, _, store, inventory) = Setup();
        sale.DeliveryChargeUzs = 20;
        await inventory.ReserveAsync(sale);
        var payments = new SalesPaymentService(store);
        await payments.AddAsync(sale, new() { Type = PaymentOperationType.Payment, Status = PaymentStatus.Completed,
            Method = PaymentMethod.Cash, AmountUzs = 220 });
        Assert.Equal(SaleStatus.Reserved, sale.Status);
        await inventory.CancelAsync(sale);
        Assert.Equal(220, sale.RefundDueUzs);
        Assert.Equal(0, sale.BalanceDueUzs);
        var refund = new SalePayment { Type = PaymentOperationType.Refund, Status = PaymentStatus.Completed,
            Method = PaymentMethod.Cash, AmountUzs = 220 };
        await payments.AddAsync(sale, refund);
        await payments.AddAsync(sale, refund);
        Assert.Equal(0, sale.RefundDueUzs);
        Assert.Empty(sale.Returns);
        Assert.Equal(SaleStatus.Cancelled, sale.Status);
    }

    [Fact]
    public async Task Return_before_completion_uses_discount_and_explicit_delivery_refund()
    {
        var (sale, catalog, store, inventory) = Setup();
        sale.DiscountMode = SaleDiscountMode.Percent;
        sale.DiscountPercent = 10;
        sale.DiscountUzs = 50; // Inactive input must not add a second discount.
        sale.DeliveryChargeUzs = 20;
        Assert.Equal(200, sale.TotalUzs);
        await inventory.ReserveAsync(sale);
        await inventory.MarkShippedAsync(sale);
        var document = new SaleReturn { Reason = "Размер", RefundAmountUzs = 999,
            Items = [new() { SaleItemId = sale.Items[0].Id, Quantity = 1, Disposition = ReturnDisposition.Restock }] };
        var returns = new SalesReturnService(catalog, store);
        await returns.CreateAsync(sale, document);
        await returns.CreateAsync(sale, document);
        Assert.Equal(90, document.RefundAmountUzs);
        Assert.Equal(110, sale.BalanceDueUzs);
        Assert.Equal(0, sale.RefundDueUzs); // No money has been paid.
        Assert.Equal(2, (await catalog.GetProductsAsync())[0].Quantity);
        Assert.Equal(SaleStatus.Shipped, sale.Status);
        await returns.CreateAsync(sale, new() { Reason = "Остальное", DeliveryRefundUzs = 20,
            Items = [new() { SaleItemId = sale.Items[0].Id, Quantity = 1, Disposition = ReturnDisposition.Restock }] });
        Assert.Equal(0, sale.NetTotalUzs);
        Assert.Equal(SaleStatus.Returned, sale.Status);
        Assert.Equal(3, (await catalog.GetProductsAsync())[0].Quantity);
    }
}
