using System.Text.Json;
using Dona.Crm.Storage.Browser;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;

namespace Dona.Crm.Storage.Browser.Tests;

public sealed partial class BrowserCrmRepositoryTests
{
    private static Product PricingProduct(string sku)
    {
        var product = new Product { Name = sku, Sku = sku, Variants = [new()] };
        StockLayerOperations.Add(product.Variants[0], 10, 1000, StockLayerSource.OpeningBalance, DateTimeOffset.UtcNow, "OPENING");
        product.Variants[0].ReservedQuantity = 2;
        return product;
    }

    [Fact]
    public async Task Pricing_is_atomic_idempotent_preserves_stock_sales_and_survives_backup()
    {
        var js = new KeyValueJsRuntime();
        var repo = new BrowserCrmRepository(js);
        var a = PricingProduct("A"); var b = PricingProduct("B");
        await repo.UpsertProductAsync(a); await repo.UpsertProductAsync(b);
        var sale = new Sale { Items = [new() { ProductId = a.Id, UnitPriceUzs = 80, Quantity = 1 }] };
        await repo.UpsertSaleAsync(sale);
        var pricing = new ProductPricingService(repo, repo, repo, repo);
        var q1 = await pricing.QuoteAsync(a.Id, PricingBasis.RemainingStock);
        var q2 = await pricing.QuoteAsync(b.Id, PricingBasis.RemainingStock);
        var request = new PricingRequest(Guid.NewGuid(), PricingSource.Catalog, null,
            [new(q1, 150, PricingInput.Markup, 50, 1), new(q2, 170, PricingInput.ManualPrice, null, 1)]);
        var before = await repo.ReadSnapshotAsync();
        js.QuotaExceeded = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => pricing.ApplyAsync(request));
        js.QuotaExceeded = false;
        Assert.Equal(DonaSyncFingerprint.Create(before), DonaSyncFingerprint.Create(await repo.ReadSnapshotAsync()));
        await pricing.ApplyAsync(request);
        await pricing.ApplyAsync(request);
        Assert.Single(await repo.GetPriceChangesAsync());
        Assert.Equal(150, (await repo.GetProductAsync(a.Id))!.SellingPriceUzs);
        Assert.Equal(JsonSerializer.Serialize(a.Variants), JsonSerializer.Serialize((await repo.GetProductAsync(a.Id))!.Variants));
        Assert.Equal(80, (await repo.GetSaleAsync(sale.Id))!.Items[0].UnitPriceUzs);
        Assert.Empty((await repo.ReadSnapshotAsync()).StockMovements);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pricing.ApplyAsync(request with { Lines = [request.Lines[0] with { NewPrice = 151 }] }));
        var snapshot = await repo.ReadSnapshotAsync();
        var bytes = BackupArchiveCodec.Create(BackupSnapshotMapper.FromSyncSnapshot(snapshot));
        var targetJs = new KeyValueJsRuntime();
        var target = new BrowserCrmRepository(targetJs);
        await new BackupRestoreService(target, new BrowserLocalImageStore(targetJs)).RestoreAsync(bytes);
        Assert.Equal(DonaSyncFingerprint.Create(snapshot), DonaSyncFingerprint.Create(await new BrowserCrmRepository(targetJs).ReadSnapshotAsync()));
    }

    [Theory]
    [InlineData("price")] [InlineData("reserve")] [InlineData("cost")] [InlineData("stock")]
    public async Task Changed_source_rejects_whole_pricing_batch(string change)
    {
        var repo = new BrowserCrmRepository(new KeyValueJsRuntime());
        var a = PricingProduct("A"); var b = PricingProduct("B");
        await repo.UpsertProductAsync(a); await repo.UpsertProductAsync(b);
        var pricing = new ProductPricingService(repo, repo, repo, repo);
        var qa = await pricing.QuoteAsync(a.Id, PricingBasis.RemainingStock);
        var qb = await pricing.QuoteAsync(b.Id, PricingBasis.RemainingStock);
        if (change == "price") b.SellingPriceUzs = 130;
        if (change == "reserve") b.Variants[0].ReservedQuantity++;
        if (change == "cost") { b.Variants[0].Layers[0].InitialValue = b.Variants[0].Layers[0].RemainingValue = 1100; b.Variants[0].Layers[0].ValuationRevision++; }
        if (change == "stock") StockLayerOperations.Add(b.Variants[0], 1, 100, StockLayerSource.OpeningBalance, DateTimeOffset.UtcNow, "NEW");
        await repo.UpsertProductAsync(b);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pricing.ApplyAsync(new(Guid.NewGuid(), PricingSource.Catalog, null, [new(qa, 150, PricingInput.ManualPrice, null, 1), new(qb, 150, PricingInput.ManualPrice, null, 1)])));
        Assert.Null((await repo.GetProductAsync(a.Id))!.SellingPriceUzs);
        Assert.Empty(await repo.GetPriceChangesAsync());
    }

    [Fact]
    public async Task Late_cost_requires_review_and_does_not_mutate_history()
    {
        var repo = new BrowserCrmRepository(new KeyValueJsRuntime());
        var p = PricingProduct("A"); await repo.UpsertProductAsync(p);
        var service = new ProductPricingService(repo, repo, repo, repo);
        var quote = await service.QuoteAsync(p.Id, PricingBasis.RemainingStock);
        var operation = await service.ApplyAsync(new(Guid.NewGuid(), PricingSource.Product, p.Id, [new(quote, 150, PricingInput.ManualPrice, null, 1)]));
        var line = operation.Lines[0];
        Assert.False(await service.NeedsReviewAsync(line));
        p = (await repo.GetProductAsync(p.Id))!;
        p.Variants[0].Layers[0].InitialValue = p.Variants[0].Layers[0].RemainingValue = 1200;
        p.Variants[0].Layers[0].ValuationRevision++;
        await repo.UpsertProductAsync(p);
        Assert.True(await service.NeedsReviewAsync(line));
        Assert.Equal(100, (await repo.GetPriceChangesAsync()).Single().Lines[0].Quote.UnitCost);
        Assert.Equal(150, (await repo.GetProductAsync(p.Id))!.SellingPriceUzs);
    }
}
