using System.Text.Json;
using Dona.Crm.Web.Domain;

namespace Dona.Crm.Core.Tests;

public sealed class PurchaseItemAmountsTests
{
    [Fact]
    public void Quantity_changes_recalculate_totals_preserving_unit_values()
    {
        var item = new PurchaseItem { Quantity = 3, UnitPrice = 8, UnitWeightKg = .395m };
        Assert.Equal(24m, item.TotalPrice);
        Assert.Equal(1.185m, item.TotalWeightKg);
        item.Quantity = 5;
        Assert.Equal(40m, item.TotalPrice);
        Assert.Equal(1.975m, item.TotalWeightKg);
        Assert.Equal(8m, item.UnitPrice);
        Assert.Equal(.395m, item.UnitWeightKg);
    }

    [Fact]
    public void Totals_update_unit_values_and_survive_serialization_without_duplicate_fields()
    {
        var item = new PurchaseItem { Quantity = 3, TotalPrice = 100m, TotalWeightKg = 1m };
        Assert.Equal(100m / 3, item.UnitPrice);
        Assert.Equal(1m / 3, item.UnitWeightKg);
        var json = JsonSerializer.Serialize(item);
        Assert.DoesNotContain("TotalPrice", json);
        Assert.DoesNotContain("TotalWeightKg", json);
        var restored = JsonSerializer.Deserialize<PurchaseItem>(json)!;
        Assert.Equal(100m, restored.TotalPrice);
        Assert.Equal(1m, restored.TotalWeightKg);
        restored.UnitPrice = 8;
        Assert.Equal(24m, restored.TotalPrice);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Invalid_quantity_does_not_divide_or_erase_existing_unit_values(int? quantity)
    {
        var item = new PurchaseItem { Quantity = quantity, UnitPrice = 8, UnitWeightKg = .5m };
        Assert.Null(item.TotalPrice);
        Assert.Null(item.TotalWeightKg);
        Assert.Throws<InvalidOperationException>(() => item.TotalPrice = 10);
        Assert.Throws<InvalidOperationException>(() => item.TotalWeightKg = 1);
        Assert.Equal(8m, item.UnitPrice);
        Assert.Equal(.5m, item.UnitWeightKg);
    }

    [Fact]
    public void Empty_totals_clear_values_and_zero_remains_a_known_value()
    {
        var item = new PurchaseItem { Quantity = 2, TotalPrice = 0, TotalWeightKg = 0 };
        Assert.Equal(0m, item.UnitPrice);
        Assert.Equal(0m, item.TotalPrice);
        Assert.Equal(0m, item.TotalWeightKg);
        item.TotalPrice = null;
        item.TotalWeightKg = null;
        Assert.Null(item.UnitPrice);
        Assert.Null(item.UnitWeightKg);
    }
}
