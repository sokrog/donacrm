using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Services;

public static class LossCapitalization
{
    // Transfer only to physical stock held now. Past FIFO issues remain immutable.
    public static void Apply(IEnumerable<StockLayer> source, decimal amount, Guid operationId,
        Guid? sourceItemId, List<StockValuationEvent> events)
    {
        if (amount < 0) throw new InvalidOperationException("Сумма распределения не может быть отрицательной.");
        if (amount == 0) return;
        var layers = source.Where(x => x.RemainingQuantity > 0).OrderBy(x => x.Id).ToList();
        if (layers.Count == 0) throw new InvalidOperationException("Нет остатка для распределения. Выберите отдельный расход.");
        if (layers.Any(x => x.InitialValue is null || x.RemainingValue is null))
            throw new InvalidOperationException("Сначала оцените все партии, на которые распределяется потеря.");
        var total = layers.Sum(x => x.RemainingValue!.Value);
        var byQuantity = total == 0;
        if (byQuantity) total = layers.Sum(x => x.RemainingQuantity);
        decimal cumulative = 0, allocated = 0;
        foreach (var layer in layers)
        {
            cumulative += byQuantity ? layer.RemainingQuantity : layer.RemainingValue!.Value;
            var next = Math.Round(amount * cumulative / total, 2, MidpointRounding.AwayFromZero);
            var delta = next - allocated;
            allocated = next;
            if (delta == 0) continue;
            events.Add(new(Guid.NewGuid(), operationId, DateTimeOffset.UtcNow, StockValuationReason.LossCapitalization,
                layer.Id, sourceItemId, layer.ValuationRevision, layer.ValuationRevision + 1,
                layer.InitialValue, layer.InitialValue + delta, delta, -delta));
            layer.CapitalizedLossValue += delta;
            layer.InitialValue += delta;
            layer.RemainingValue += delta;
            layer.ValuationRevision++;
        }
    }
}
