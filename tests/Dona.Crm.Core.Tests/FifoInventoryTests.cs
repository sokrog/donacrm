using System.Text.Json;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;

namespace Dona.Crm.Core.Tests;

public sealed class FifoInventoryTests
{
    private static readonly DateTimeOffset Received = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static StockLayer Layer(int quantity, decimal? amount, int day = 0) => new()
    {
        Source = StockLayerSource.PurchaseReceipt, ReceivedAt = Received.AddDays(day),
        InitialQuantity = quantity, RemainingQuantity = quantity, InitialValue = amount, RemainingValue = amount
    };

    private static ProductVariant Variant(params StockLayer[] layers) => new()
    {
        StockLayerVersion = FifoCostCalculator.CurrentVersion,
        Quantity = layers.Sum(x => x.RemainingQuantity), Layers = layers.ToList()
    };

    [Fact]
    public void Snapshot_and_backup_require_supported_fifo_format()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DonaSyncSnapshot>("{}"));
        Assert.Throws<InvalidDataException>(() => new DonaSyncSnapshot { SchemaVersion = 1 }.ValidateFormat());
        Assert.Throws<InvalidDataException>(() => new DonaSyncSnapshot { SchemaVersion = 99 }.ValidateFormat());
        var variant = Variant(Layer(3, 100));
        FifoCostCalculator.Consume(variant, 1);
        var snapshot = new DonaSyncSnapshot { Products = [new() { Variants = [variant] }] };
        var archive = BackupArchiveCodec.Create(BackupSnapshotMapper.FromSyncSnapshot(snapshot));
        var restored = BackupSnapshotMapper.ToSyncSnapshot(BackupArchiveCodec.Inspect(archive).Snapshot);
        Assert.Equal(DonaSyncFingerprint.Create(snapshot), DonaSyncFingerprint.Create(restored));
        var oldArchive = BackupArchiveCodec.Create(new BackupSnapshot { SchemaVersion = 1 });
        Assert.Throws<InvalidOperationException>(() => BackupArchiveCodec.Inspect(oldArchive));
    }

    [Fact]
    public void Preview_and_issue_cross_layers_without_changing_preview_input()
    {
        var first = Layer(3, 300);
        var second = Layer(10, 1500, 19);
        var variant = Variant(second, first);
        var before = JsonSerializer.Serialize(variant);

        var preview = FifoCostCalculator.Preview(variant, 5);

        Assert.Equal(before, JsonSerializer.Serialize(variant));
        Assert.Equal(new[] { first.Id, second.Id }, preview.Select(x => x.LayerId));
        Assert.Equal(new[] { 3, 2 }, preview.Select(x => x.Quantity));
        Assert.Equal(600m, FifoCostCalculator.Cost(preview).TotalValue);

        var issued = FifoCostCalculator.Consume(variant, 5);

        Assert.Equal(preview.Select(x => (x.LayerId, x.Quantity, x.TotalCost)), issued.Select(x => (x.LayerId, x.Quantity, x.TotalCost)));
        Assert.Equal(8, variant.Quantity);
        Assert.Equal(0, first.RemainingQuantity);
        Assert.Equal(8, second.RemainingQuantity);
        Assert.Equal(1200m, FifoCostCalculator.Value(variant).TotalValue);
        Assert.Equal(2, variant.Layers.Count);
    }

    [Fact]
    public void Partial_return_claims_last_consumption_and_uses_actual_cost()
    {
        var variant = Variant(Layer(3, 300), Layer(10, 1500, 1));
        var issued = FifoCostCalculator.Consume(variant, 5);
        var firstReturn = FifoCostCalculator.PreviewReturn(issued, [], 1);
        var part = Assert.Single(firstReturn);

        Assert.Equal(issued[1].Id, part.ConsumptionId);
        Assert.Equal(150m, part.OriginalCost);
        Assert.Equal(450m, FifoCostCalculator.Cost(issued).TotalValue - part.OriginalCost);

        var remainingReturn = FifoCostCalculator.PreviewReturn(issued, firstReturn, 4);
        Assert.Equal(new[] { 1, 3 }, remainingReturn.Select(x => x.Quantity));
        Assert.Equal(450m, remainingReturn.Sum(x => x.OriginalCost));
        Assert.Throws<InventoryException>(() => FifoCostCalculator.PreviewReturn(issued, firstReturn.Concat(remainingReturn), 1));
    }

    [Fact]
    public void Previously_accepted_defective_units_cannot_be_claimed_again()
    {
        var issued = FifoCostCalculator.Consume(Variant(Layer(2, 200)), 2);
        var defective = FifoCostCalculator.PreviewReturn(issued, [], 1);
        Assert.Single(FifoCostCalculator.PreviewReturn(issued, defective, 1));
        Assert.Throws<InventoryException>(() => FifoCostCalculator.PreviewReturn(issued, defective, 2));
    }

    [Fact]
    public void Unknown_cost_is_not_zero_and_does_not_skip_the_oldest_layer()
    {
        var unknown = Layer(2, null);
        var variant = Variant(unknown, Layer(2, 0, 1), Layer(2, 200, 2));

        var cost = FifoCostCalculator.Cost(FifoCostCalculator.Consume(variant, 5));

        Assert.False(cost.IsComplete);
        Assert.Null(cost.TotalValue);
        Assert.Equal(100m, cost.KnownValue);
        Assert.Equal(2, cost.UnvaluedQuantity);
        Assert.Equal(0, unknown.RemainingQuantity);
        Assert.Equal(100m, FifoCostCalculator.Value(variant).TotalValue);
    }

    [Fact]
    public void Zero_cost_is_fully_known()
    {
        var cost = FifoCostCalculator.Cost(FifoCostCalculator.Consume(Variant(Layer(2, 0)), 2));
        Assert.True(cost.IsComplete);
        Assert.Equal(0m, cost.TotalValue);
    }

    [Fact]
    public void Equal_dates_use_sequence_then_id_after_serialization()
    {
        var a = Layer(1, 10);
        var b = Layer(1, 20);
        var c = Layer(1, 30);
        a.Id = Guid.Parse("00000000-0000-0000-0000-000000000001");
        b.Id = Guid.Parse("00000000-0000-0000-0000-000000000002");
        c.Sequence = 1;
        var original = Variant(c, b, a);
        var restored = JsonSerializer.Deserialize<ProductVariant>(JsonSerializer.Serialize(original))!;

        Assert.Equal(new[] { a.Id, b.Id, c.Id }, FifoCostCalculator.Preview(restored, 3).Select(x => x.LayerId));
    }

    [Fact]
    public void Unknown_migration_date_sorts_first_without_inventing_an_age()
    {
        var old = Layer(1, 10);
        old.ReceivedAt = null;
        old.Source = StockLayerSource.Migration;
        old.IsApproximate = true;
        Assert.Equal(old.Id, Assert.Single(FifoCostCalculator.Preview(Variant(Layer(1, 20), old), 1)).LayerId);
        Assert.Null(old.ReceivedAt);
    }

    [Fact]
    public void Own_reserve_is_released_and_other_reserves_remain_available_to_their_owners()
    {
        var variant = Variant(Layer(10, 1000));
        variant.ReservedQuantity = 7;
        FifoCostCalculator.Consume(variant, 5, ownReservedQuantity: 3);

        Assert.Equal(5, variant.Quantity);
        Assert.Equal(4, variant.ReservedQuantity);
        Assert.Equal(1, variant.AvailableQuantity);
        FifoCostCalculator.Validate(variant);
    }

    [Fact]
    public void Insufficient_stock_does_not_mutate_any_layers_or_reservations()
    {
        var variant = Variant(Layer(2, 200), Layer(3, 600, 1));
        variant.ReservedQuantity = 2;
        var before = JsonSerializer.Serialize(variant);

        Assert.Throws<InventoryException>(() => FifoCostCalculator.Consume(variant, 4));
        Assert.Equal(before, JsonSerializer.Serialize(variant));
    }

    [Fact]
    public void Multiple_order_lines_share_one_scratch_balance()
    {
        var variant = Variant(Layer(3, 300), Layer(3, 600, 1));
        variant.ReservedQuantity = 4;
        var before = JsonSerializer.Serialize(variant);
        var order = FifoCostCalculator.PreviewOrder([new(variant, 2, 2), new(variant, 3, 2)]);

        Assert.Equal(200m, FifoCostCalculator.Cost(order[0]).TotalValue);
        Assert.Equal(500m, FifoCostCalculator.Cost(order[1]).TotalValue);
        Assert.Equal(before, JsonSerializer.Serialize(variant));
        Assert.Throws<InventoryException>(() => FifoCostCalculator.PreviewOrder([new(variant, 4, 2), new(variant, 3, 2)]));
        Assert.Equal(before, JsonSerializer.Serialize(variant));
    }

    [Fact]
    public void Two_different_snapshots_for_one_variant_are_rejected()
    {
        var variant = Variant(Layer(2, 200));
        var other = Variant(Layer(2, 200));
        other.Id = variant.Id;
        Assert.Throws<InventoryException>(() => FifoCostCalculator.PreviewOrder([new(variant, 1), new(other, 1)]));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 2)]
    public void Invalid_issue_arguments_are_rejected(int quantity, int ownReserved)
    {
        var variant = Variant(Layer(3, 300));
        Assert.Throws<InventoryException>(() => FifoCostCalculator.Consume(variant, quantity, ownReserved));
        Assert.Equal(3, variant.Quantity);
    }

    [Fact]
    public void Legacy_stock_requires_explicit_migration_even_when_empty()
    {
        Assert.Throws<InventoryException>(() => FifoCostCalculator.Value(new ProductVariant { Quantity = 0 }));
        Assert.Equal(0m, FifoCostCalculator.Value(Variant()).TotalValue);
    }

    [Theory]
    [InlineData("quantity")]
    [InlineData("reserved")]
    [InlineData("duplicate")]
    [InlineData("negative")]
    [InlineData("exhausted-value")]
    [InlineData("partial-unknown")]
    [InlineData("precision")]
    [InlineData("future-version")]
    public void Invalid_layers_fail_before_mutation(string defect)
    {
        var layer = Layer(2, 200);
        var variant = Variant(layer);
        switch (defect)
        {
            case "quantity": variant.Quantity = 3; break;
            case "reserved": variant.ReservedQuantity = 3; break;
            case "duplicate": variant.Layers.Add(layer); variant.Quantity = 4; break;
            case "negative": layer.RemainingQuantity = -1; break;
            case "exhausted-value": layer.RemainingQuantity = 0; variant.Quantity = 0; break;
            case "partial-unknown": layer.RemainingValue = null; break;
            case "precision": layer.InitialValue = layer.RemainingValue = 0.001m; break;
            case "future-version": variant.StockLayerVersion = 999; break;
        }
        var before = JsonSerializer.Serialize(variant);
        Assert.Throws<InventoryException>(() => FifoCostCalculator.Consume(variant, 1));
        Assert.Equal(before, JsonSerializer.Serialize(variant));
    }

    [Fact]
    public void Separate_issues_and_returns_preserve_the_last_cent()
    {
        var variant = Variant(Layer(7, 1));
        var consumed = new List<LayerConsumption>();
        for (var i = 0; i < 7; i++) consumed.AddRange(FifoCostCalculator.Consume(variant, 1));
        Assert.Equal(1m, consumed.Sum(x => x.TotalCost));
        Assert.Equal(0m, FifoCostCalculator.Value(variant).TotalValue);

        var original = FifoCostCalculator.Consume(Variant(Layer(7, 1)), 7);
        var returned = new List<LayerReturnAllocation>();
        for (var i = 0; i < 7; i++) returned.AddRange(FifoCostCalculator.PreviewReturn(original, returned, 1));
        Assert.Equal(1m, returned.Sum(x => x.OriginalCost));
    }

    [Fact]
    public void Cumulative_allocation_reconciles_receipt_documents()
    {
        var amounts = Enumerable.Range(0, 6).Select(i => FifoCostCalculator.Allocate(1m, 6, i, 1)).ToList();
        Assert.Equal(1m, amounts.Sum());
        Assert.Equal(0.17m, amounts[0]);
        Assert.Equal(0m, FifoCostCalculator.Allocate(0, 1, 0, 1));
        Assert.Null(FifoCostCalculator.Allocate(null, 1, 0, 1));
        Assert.Equal(0.01m, FifoCostCalculator.Allocate(0.03m, 6, 0, 1));
    }

    [Fact]
    public void Return_rejects_forged_links_amounts_and_overclaimed_quantities()
    {
        var issued = FifoCostCalculator.Consume(Variant(Layer(2, 200)), 2);
        var valid = Assert.Single(FifoCostCalculator.PreviewReturn(issued, [], 1));
        Assert.Throws<InventoryException>(() => FifoCostCalculator.PreviewReturn(issued, [valid with { LayerId = Guid.NewGuid() }], 1));
        Assert.Throws<InventoryException>(() => FifoCostCalculator.PreviewReturn(issued, [valid with { OriginalCost = 1 }], 1));
        Assert.Throws<InventoryException>(() => FifoCostCalculator.PreviewReturn(issued, [valid with { Quantity = 3 }], 1));
        Assert.Throws<InventoryException>(() => FifoCostCalculator.PreviewReturn(issued, [], 3));
        Assert.Throws<InventoryException>(() => FifoCostCalculator.PreviewReturn(issued, [], 0));
    }

    [Fact]
    public void Unknown_return_preserves_unknown_original_cost()
    {
        var issued = FifoCostCalculator.Consume(Variant(Layer(2, null)), 2);
        Assert.Null(Assert.Single(FifoCostCalculator.PreviewReturn(issued, [], 1)).OriginalCost);
    }

    [Fact]
    public void Consumption_is_a_snapshot_independent_of_subsequent_layer_valuation()
    {
        var layer = Layer(2, 200);
        var issued = Assert.Single(FifoCostCalculator.Consume(Variant(layer), 1));
        layer.InitialValue = 240;
        layer.RemainingValue = 120;
        layer.ValuationRevision++;

        Assert.Equal(100m, issued.TotalCost);
        Assert.Equal(100m, issued.UnitCost);
        Assert.Equal(0, issued.ValuationRevision);
    }

    [Fact]
    public async Task Rollback_restores_layers_in_place_after_consumption_failure()
    {
        var layer = Layer(3, 300);
        var variant = Variant(layer);
        var product = new Product { Variants = [variant] };

        await Assert.ThrowsAsync<IOException>(() => EntityRollback.RunAsync(product, () =>
        {
            FifoCostCalculator.Consume(variant, 2);
            return Task.FromException(new IOException("Storage failure"));
        }));

        Assert.Same(variant, Assert.Single(product.Variants));
        Assert.Same(layer, Assert.Single(variant.Layers));
        Assert.Equal(3, variant.Quantity);
        Assert.Equal(3, layer.RemainingQuantity);
        Assert.Equal(300m, layer.RemainingValue);
    }

    [Fact]
    public void Sync_json_preserves_layer_links_cost_snapshots_returns_and_valuation_events()
    {
        var layer = Layer(3, 300);
        layer.PurchaseId = Guid.NewGuid();
        layer.PurchaseItemId = Guid.NewGuid();
        layer.ReceiptId = Guid.NewGuid();
        layer.ReceiptLineId = Guid.NewGuid();
        var variant = Variant(layer);
        var issued = FifoCostCalculator.Consume(variant, 1);
        var returns = FifoCostCalculator.PreviewReturn(issued, [], 1);
        var valuation = new StockValuationEvent(Guid.NewGuid(), Guid.NewGuid(), Received,
            StockValuationReason.Revaluation, layer.Id, layer.PurchaseItemId, 0, 1, 300, 360, 40, 20);
        var snapshot = new DonaSyncSnapshot
        {
            Products = [new() { Variants = [variant], StockValuations = [valuation] }],
            Sales = [new() { Items = [new() { Consumptions = issued.ToList() }],
                Returns = [new() { Items = [new() { LayerAllocations = returns.ToList() }] }] }],
            Purchases = [new() { StockValuations = [valuation], ShortageSettlements =
                [new(Guid.NewGuid(), layer.PurchaseItemId.Value, Received, 2, 200, 120, 80)] }]
        };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var settlementId = snapshot.Purchases[0].ShortageSettlements[0].Id;
        snapshot.Purchases[0].CompensationCorrections.Add(new(Guid.NewGuid(), settlementId, Received, 120, 0, "Возврат компенсации"));
        snapshot.Purchases[0].LateReceipts.Add(new(Guid.NewGuid(), settlementId, Guid.NewGuid(), Received, 1, 0, "Поздняя поставка"));
        var restored = JsonSerializer.Deserialize<DonaSyncSnapshot>(JsonSerializer.Serialize(snapshot, options), options)!;
        var actual = restored.Products[0].Variants[0].Layers[0];

        Assert.Equal(layer.ReceiptLineId, actual.ReceiptLineId);
        Assert.Equal(layer.PurchaseItemId, actual.PurchaseItemId);
        Assert.Equal(200m, actual.RemainingValue);
        Assert.Equal(issued[0], restored.Sales[0].Items[0].Consumptions[0]);
        Assert.Equal(returns[0], restored.Sales[0].Returns[0].Items[0].LayerAllocations[0]);
        Assert.Equal(valuation, restored.Products[0].StockValuations[0]);
        Assert.Equal(valuation, restored.Purchases[0].StockValuations[0]);
        Assert.Equal(80m, restored.Purchases[0].ShortageSettlements[0].Loss);
        Assert.Equal(DonaSyncFingerprint.Create(snapshot), DonaSyncFingerprint.Create(restored));
        var archive = BackupArchiveCodec.Create(BackupSnapshotMapper.FromSyncSnapshot(snapshot));
        var fromZip = BackupSnapshotMapper.ToSyncSnapshot(BackupArchiveCodec.Inspect(archive).Snapshot);
        Assert.Equal(DonaSyncFingerprint.Create(snapshot), DonaSyncFingerprint.Create(fromZip));
        Assert.Equal(snapshot.Purchases[0].CompensationCorrections[0], fromZip.Purchases[0].CompensationCorrections[0]);
        Assert.Equal(snapshot.Purchases[0].LateReceipts[0], fromZip.Purchases[0].LateReceipts[0]);
    }
}
