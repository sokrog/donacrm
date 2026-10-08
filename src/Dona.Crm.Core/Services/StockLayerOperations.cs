using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Services;

public static class StockLayerOperations
{
    public static void PrepareEmpty(ProductVariant variant)
    {
        if (variant.StockLayerVersion == 0 && (variant.Quantity ?? 0) == 0 && variant.ReservedQuantity == 0 && variant.Layers.Count == 0)
        {
            variant.Quantity = 0;
            variant.StockLayerVersion = FifoCostCalculator.CurrentVersion;
        }
        FifoCostCalculator.Validate(variant);
    }

    public static StockLayer Add(ProductVariant variant, int quantity, decimal? total, StockLayerSource source,
        DateTimeOffset at, string number, Guid? id = null)
    {
        PrepareEmpty(variant);
        if (quantity <= 0 || total < 0) throw new InventoryException("Количество и стоимость прихода некорректны.");
        var layer = new StockLayer
        {
            Id = id ?? Guid.NewGuid(), Source = source, ReceivedAt = at, SourceNumber = number,
            InitialQuantity = quantity, RemainingQuantity = quantity,
            InitialValue = total is null ? null : Math.Round(total.Value, 2, MidpointRounding.AwayFromZero),
            RemainingValue = total is null ? null : Math.Round(total.Value, 2, MidpointRounding.AwayFromZero)
        };
        if (variant.Layers.Any(x => x.Id == layer.Id)) throw new InventoryException("Этот приход уже записан.");
        variant.Layers.Add(layer);
        variant.Quantity = checked(variant.Quantity!.Value + quantity);
        return layer;
    }

    public static StockCostSummary Restore(ProductVariant variant, IReadOnlyList<LayerConsumption> original, IReadOnlyList<LayerReturnAllocation> allocations)
    {
        FifoCostCalculator.Validate(variant);
        var changes = allocations.GroupBy(x => x.LayerId).Select(group =>
        {
            var layer = variant.Layers.SingleOrDefault(x => x.Id == group.Key)
                ?? throw new InventoryException("Исходная партия возврата не найдена.");
            var quantity = group.Sum(x => x.Quantity);
            if (quantity <= 0 || quantity > layer.InitialQuantity - layer.RemainingQuantity)
                throw new InventoryException("Возврат превышает списанное количество партии.");
            var revalued = group.Any(x => original.Single(c => c.Id == x.ConsumptionId).ValuationRevision != layer.ValuationRevision);
            decimal? value = revalued
                ? FifoCostCalculator.Allocate(layer.InitialValue - layer.RemainingValue, layer.InitialQuantity - layer.RemainingQuantity, 0, quantity)
                : group.Any(x => x.OriginalCost is null) ? null : group.Sum(x => x.OriginalCost!.Value);
            return (layer, quantity, value);
        }).ToList();
        foreach (var (layer, quantity, value) in changes)
        {
            layer.RemainingQuantity += quantity;
            layer.RemainingValue += value;
            variant.Quantity += quantity;
        }
        FifoCostCalculator.Validate(variant);
        return new(changes.Sum(x => x.value ?? 0), changes.Where(x => x.value is null).Sum(x => x.quantity));
    }

    public static void SetValue(StockMovement movement, StockCostSummary cost, int sign)
    {
        movement.ValueDelta = cost.TotalValue * sign;
        movement.KnownValueDelta = cost.KnownValue * sign;
        movement.UnvaluedQuantity = cost.UnvaluedQuantity;
    }
}
