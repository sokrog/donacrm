using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Services;

public sealed record SaleFinancialEvent(Sale Sale, SaleItem Item, Guid? LayerId, DateTimeOffset At,
    int Quantity, decimal Revenue, decimal Cost, bool IsSale, bool UnknownCost);

/// <summary>Sale and return facts retain their own recognition dates. All reports use this projection.</summary>
public static class SaleFinancialEvents
{
    public static IReadOnlyList<SaleFinancialEvent> Build(IEnumerable<Sale> source)
    {
        var result = new List<SaleFinancialEvent>();
        foreach (var sale in source.Where(x => x.Status == SaleStatus.Shipped || x.Status == SaleStatus.Completed || x.Status == SaleStatus.Returned && x.Returns.Count > 0))
        {
            var weights = sale.Items.Select(x => (x.UnitPriceUzs ?? 0) * (x.SoldQuantity > 0 ? x.SoldQuantity : x.Quantity ?? 0)).ToArray();
            var revenues = Distribute(sale.TotalUzs, weights);
            for (var i = 0; i < sale.Items.Count; i++)
            {
                var item = sale.Items[i];
                var sold = item.SoldQuantity > 0 ? item.SoldQuantity : item.Quantity ?? 0;
                if (item.Consumptions.Count == 0)
                    result.Add(new(sale, item, null, sale.ShippedAt ?? sale.CompletedAt ?? sale.CreatedAt, sold, revenues[i],
                        (item.UnitCostUzs ?? 0) * sold, true, item.UnitCostUzs is null));
                else
                {
                    var parts = Distribute(revenues[i], item.Consumptions.Select(x => (decimal)x.Quantity).ToArray());
                    for (var j = 0; j < item.Consumptions.Count; j++)
                    {
                        var part = item.Consumptions[j];
                        result.Add(new(sale, item, part.LayerId, sale.ShippedAt ?? sale.CompletedAt ?? sale.CreatedAt, part.Quantity,
                            parts[j], part.TotalCost ?? 0, true, part.TotalCost is null));
                    }
                }
            }
            foreach (var document in sale.Returns)
            {
                var lines = document.Items.Where(x => x.Disposition != ReturnDisposition.Rejected)
                    .Select(x => (Line: x, Item: sale.Items.SingleOrDefault(item => item.Id == x.SaleItemId)))
                    .Where(x => x.Item is not null).ToList();
                if (lines.Count == 0)
                {
                    // Historical money-only return documents still belong to their own date.
                    var amounts = Distribute(document.RefundAmountUzs ?? 0, weights);
                    for (var i = 0; i < sale.Items.Count; i++)
                        result.Add(new(sale, sale.Items[i], null, document.CreatedAt, 0, -amounts[i], 0, false, false));
                    continue;
                }
                var refunds = Distribute(document.RefundAmountUzs ?? 0, lines.Select(x => (x.Item!.UnitPriceUzs ?? 0) * (x.Line.Quantity ?? 0)).ToArray());
                for (var i = 0; i < lines.Count; i++)
                {
                    var (line, item) = lines[i];
                    var restock = line.Disposition == ReturnDisposition.Restock;
                    if (line.LayerAllocations.Count == 0)
                        result.Add(new(sale, item!, null, document.CreatedAt, -(line.Quantity ?? 0), -refunds[i],
                            restock ? -(item!.UnitCostUzs ?? 0) * (line.Quantity ?? 0) : 0, false, restock && item!.UnitCostUzs is null));
                    else
                    {
                        var parts = Distribute(refunds[i], line.LayerAllocations.Select(x => (decimal)x.Quantity).ToArray());
                        for (var j = 0; j < line.LayerAllocations.Count; j++)
                        {
                            var part = line.LayerAllocations[j];
                            result.Add(new(sale, item!, part.LayerId, document.CreatedAt, -part.Quantity, -parts[j],
                                restock ? -(part.OriginalCost ?? 0) : 0, false, restock && part.OriginalCost is null));
                        }
                    }
                }
            }
        }
        return result;
    }

    public static bool InPeriod(DateTimeOffset at, DateTime? from, DateTime? to) =>
        (from is null || at.ToLocalTime().Date >= from.Value.Date) && (to is null || at.ToLocalTime().Date <= to.Value.Date);

    private static decimal[] Distribute(decimal amount, decimal[] weights)
    {
        if (weights.Length == 0) return [];
        var total = weights.Sum();
        if (total == 0) { weights = weights.Select(_ => 1m).ToArray(); total = weights.Length; }
        var result = new decimal[weights.Length];
        decimal previousWeight = 0, previousValue = 0;
        for (var i = 0; i < weights.Length; i++)
        {
            previousWeight += weights[i];
            var cumulative = i == weights.Length - 1 ? amount : Math.Round(amount * previousWeight / total, 2, MidpointRounding.AwayFromZero);
            result[i] = cumulative - previousValue;
            previousValue = cumulative;
        }
        return result;
    }
}
