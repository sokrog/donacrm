using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Services;

/// <summary>
/// Storage-independent FIFO arithmetic. Preview never mutates its input. Callers must load current
/// aggregates under InventoryLock and persist consumed layers and documents in one InventoryCommit.
/// </summary>
public static class FifoCostCalculator
{
    public const int CurrentVersion = 1;

    public static void Validate(ProductVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        if (variant.StockLayerVersion != CurrentVersion)
            throw new InventoryException("Для FIFO требуется совместимая версия данных склада.");
        if (variant.Quantity is null or < 0 || variant.ReservedQuantity < 0 || variant.ReservedQuantity > variant.Quantity)
            throw new InventoryException("Остаток и резерв варианта некорректны.");
        if (variant.Layers.Select(x => x.Id).Distinct().Count() != variant.Layers.Count)
            throw new InventoryException("Идентификаторы складских слоёв повторяются.");
        foreach (var layer in variant.Layers) ValidateLayer(layer);
        if (variant.Layers.Sum(x => (long)x.RemainingQuantity) != variant.Quantity)
            throw new InventoryException("Остаток варианта не совпадает с суммой слоёв.");
    }

    public static StockCostSummary Value(ProductVariant variant)
    {
        Validate(variant);
        return new(variant.Layers.Sum(x => x.RemainingValue ?? 0),
            variant.Layers.Where(x => x.RemainingValue is null).Sum(x => x.RemainingQuantity));
    }

    public static IReadOnlyList<LayerConsumption> Preview(ProductVariant variant, int quantity, int ownReservedQuantity = 0)
    {
        Validate(variant);
        if (quantity <= 0) throw new InventoryException("Количество списания должно быть положительным.");
        if (ownReservedQuantity < 0 || ownReservedQuantity > variant.ReservedQuantity || ownReservedQuantity > quantity)
            throw new InventoryException("Собственный резерв не соответствует списанию.");
        if (quantity > variant.Quantity - variant.ReservedQuantity + ownReservedQuantity)
            throw new InventoryException("Недостаточно доступного остатка для списания FIFO.");

        var result = new List<LayerConsumption>();
        var remaining = quantity;
        foreach (var layer in OrderedLayers(variant).Where(x => x.RemainingQuantity > 0))
        {
            var take = Math.Min(remaining, layer.RemainingQuantity);
            var amount = Allocate(layer.RemainingValue, layer.RemainingQuantity, 0, take);
            result.Add(new(Guid.NewGuid(), layer.Id, take, layer.UnitCost, amount, layer.ValuationRevision));
            remaining -= take;
            if (remaining == 0) break;
        }
        return result;
    }

    /// <summary>Recalculates from current state, consumes stock and releases only this operation's reserve.</summary>
    public static IReadOnlyList<LayerConsumption> Consume(ProductVariant variant, int quantity, int ownReservedQuantity = 0)
    {
        var allocation = Preview(variant, quantity, ownReservedQuantity);
        var layers = variant.Layers.ToDictionary(x => x.Id);
        // Validate and calculate everything before touching the aggregate.
        foreach (var part in allocation)
        {
            var layer = layers[part.LayerId];
            layer.RemainingQuantity -= part.Quantity;
            layer.RemainingValue -= part.TotalCost;
            if (layer.RemainingQuantity == 0 && layer.RemainingValue.HasValue) layer.RemainingValue = 0;
        }
        variant.Quantity -= quantity;
        variant.ReservedQuantity -= ownReservedQuantity;
        return allocation;
    }

    /// <summary>
    /// Quotes all lines of an order against one scratch balance per variant, including repeated lines.
    /// Input reservations belong to each line, not to the entire variant.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<LayerConsumption>> PreviewOrder(IEnumerable<FifoRequest> requests)
    {
        var lines = requests.ToList();
        if (lines.GroupBy(x => x.Variant.Id).Any(group => group.Any(x => !ReferenceEquals(x.Variant, group.First().Variant))))
            throw new InventoryException("Для одного варианта переданы разные снимки остатка.");
        var balances = lines.Select(x => x.Variant).DistinctBy(x => x.Id).ToDictionary(x => x.Id, Copy);
        var result = new List<IReadOnlyList<LayerConsumption>>();
        foreach (var line in lines)
            result.Add(Consume(balances[line.Variant.Id], line.Quantity, line.OwnReservedQuantity));
        return result;
    }

    public static StockCostSummary Cost(IEnumerable<LayerConsumption> consumptions)
    {
        var parts = consumptions.ToList();
        foreach (var part in parts) ValidateConsumption(part);
        return new(parts.Sum(x => x.TotalCost ?? 0), parts.Where(x => x.TotalCost is null).Sum(x => x.Quantity));
    }

    /// <summary>
    /// Claims the tail of the original issue, excluding all prior accepted returns, including defects.
    /// Rejected returns must not be passed as accepted allocations. No stock is changed here.
    /// </summary>
    public static IReadOnlyList<LayerReturnAllocation> PreviewReturn(
        IReadOnlyList<LayerConsumption> consumptions,
        IEnumerable<LayerReturnAllocation> acceptedReturns,
        int quantity)
    {
        if (quantity <= 0) throw new InventoryException("Количество возврата должно быть положительным.");
        if (consumptions.Select(x => x.Id).Distinct().Count() != consumptions.Count)
            throw new InventoryException("Распределения продажи повторяются.");
        foreach (var part in consumptions) ValidateConsumption(part);
        var original = consumptions.ToDictionary(x => x.Id);
        var returned = new Dictionary<Guid, int>();
        foreach (var part in acceptedReturns)
        {
            if (!original.TryGetValue(part.ConsumptionId, out var source) || source.LayerId != part.LayerId || part.Quantity <= 0)
                throw new InventoryException("Возврат не соответствует исходному списанию.");
            var previous = returned.GetValueOrDefault(part.ConsumptionId);
            if (part.Quantity > source.Quantity - previous)
                throw new InventoryException("Возвращено больше проданного количества.");
            if (part.OriginalCost != Allocate(source.TotalCost, source.Quantity, previous, part.Quantity))
                throw new InventoryException("Стоимость возврата не соответствует исходному списанию.");
            returned[part.ConsumptionId] = previous + part.Quantity;
        }
        if (quantity > consumptions.Sum(x => (long)x.Quantity - returned.GetValueOrDefault(x.Id)))
            throw new InventoryException("Количество возврата превышает невозвращенный остаток продажи.");
        var result = new List<LayerReturnAllocation>();
        var remaining = quantity;
        foreach (var source in consumptions.Reverse())
        {
            var previous = returned.GetValueOrDefault(source.Id);
            var take = Math.Min(remaining, source.Quantity - previous);
            if (take == 0) continue;
            result.Add(new(source.Id, source.LayerId, take, Allocate(source.TotalCost, source.Quantity, previous, take)));
            remaining -= take;
            if (remaining == 0) break;
        }
        return result;
    }

    // Cumulative allocation preserves the last cent across separate receipt/return documents.
    public static decimal? Allocate(decimal? total, int totalQuantity, int previousQuantity, int quantity)
    {
        if (totalQuantity <= 0 || previousQuantity < 0 || quantity < 0 || previousQuantity > totalQuantity || quantity > totalQuantity - previousQuantity)
            throw new InventoryException("Количество для распределения стоимости некорректно.");
        if (total is null) return null;
        if (total < 0 || decimal.Round(total.Value, 2) != total.Value)
            throw new InventoryException("Сумма должна быть неотрицательной с точностью до сотых.");
        // Multiply first so an exact half-cent is not lost to a repeating decimal ratio.
        decimal Cumulative(int count) => count == totalQuantity ? total.Value
            : decimal.Round(total.Value * count / totalQuantity, 2, MidpointRounding.AwayFromZero);
        return Cumulative(previousQuantity + quantity) - Cumulative(previousQuantity);
    }

    private static IEnumerable<StockLayer> OrderedLayers(ProductVariant variant) => variant.Layers
        .OrderBy(x => x.ReceivedAt ?? DateTimeOffset.MinValue).ThenBy(x => x.Sequence).ThenBy(x => x.Id);

    private static void ValidateLayer(StockLayer layer)
    {
        if (layer.Id == Guid.Empty || !Enum.IsDefined(layer.Source) || layer.InitialQuantity <= 0 || layer.RemainingQuantity < 0 || layer.RemainingQuantity > layer.InitialQuantity || layer.ValuationRevision < 0)
            throw new InventoryException("Количество или идентификатор складского слоя некорректны.");
        if (layer.InitialValue.HasValue != layer.RemainingValue.HasValue || layer.InitialValue < 0 || layer.RemainingValue < 0 || layer.RemainingValue > layer.InitialValue)
            throw new InventoryException("Оценка складского слоя некорректна.");
        if (layer.InitialValue is { } initial && decimal.Round(initial, 2) != initial || layer.RemainingValue is { } remaining && decimal.Round(remaining, 2) != remaining)
            throw new InventoryException("Суммы складского слоя должны иметь точность до сотых.");
        if (layer.RemainingQuantity == 0 && layer.RemainingValue is > 0 || layer.RemainingQuantity == layer.InitialQuantity && layer.InitialValue != layer.RemainingValue)
            throw new InventoryException("Стоимость не соответствует количеству складского слоя.");
    }

    private static void ValidateConsumption(LayerConsumption part)
    {
        if (part.Id == Guid.Empty || part.LayerId == Guid.Empty || part.Quantity <= 0 || part.ValuationRevision < 0 || part.UnitCost < 0 || part.TotalCost < 0 || part.UnitCost.HasValue != part.TotalCost.HasValue)
            throw new InventoryException("Распределение себестоимости некорректно.");
        if (part.TotalCost is { } total && decimal.Round(total, 2) != total)
            throw new InventoryException("Сумма распределения должна иметь точность до сотых.");
    }

    private static ProductVariant Copy(ProductVariant source)
    {
        Validate(source);
        return new()
        {
            Id = source.Id, Quantity = source.Quantity, ReservedQuantity = source.ReservedQuantity,
            StockLayerVersion = source.StockLayerVersion,
            Layers = source.Layers.Select(x => new StockLayer
            {
                Id = x.Id, Source = x.Source, ReceivedAt = x.ReceivedAt, Sequence = x.Sequence,
                InitialQuantity = x.InitialQuantity, RemainingQuantity = x.RemainingQuantity,
                CapitalizedLossValue = x.CapitalizedLossValue, InitialValue = x.InitialValue, RemainingValue = x.RemainingValue, ValuationRevision = x.ValuationRevision
            }).ToList()
        };
    }
}

public sealed record FifoRequest(ProductVariant Variant, int Quantity, int OwnReservedQuantity = 0);
