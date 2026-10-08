using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Services;

public sealed class IntermediaryAnalyticsService
{
    public IReadOnlyList<IntermediaryAnalytics> Calculate(IEnumerable<Intermediary> intermediaries, IEnumerable<Purchase> purchases)
    {
        var active = purchases.Where(x => x.Status is PurchaseStatus.Ordered or PurchaseStatus.ChinaWarehouse or PurchaseStatus.Shipped or PurchaseStatus.PartiallyReceived or PurchaseStatus.Received).ToList();
        return intermediaries.Select(intermediary => CalculateOne(intermediary, active.Where(x => Matches(x, intermediary)).ToList())).ToList();
    }

    private static IntermediaryAnalytics CalculateOne(Intermediary intermediary, List<Purchase> purchases)
    {
        var completed = purchases.Where(x => x.Status == PurchaseStatus.Received && x.Receipts.Count > 0).ToList();
        var deliveries = completed.Select(x =>
        {
            var received = x.Receipts.Max(r => r.ReceivedAt).ToLocalTime();
            var transit = (decimal)Math.Max(0, (received.Date - x.OrderedAt.ToLocalTime().Date).TotalDays);
            decimal? delay = x.EstimatedDeliveryDate is not null
                ? (decimal)(received.Date - x.EstimatedDeliveryDate.Value.Date).TotalDays
                : intermediary.EstimatedDays is > 0 ? transit - intermediary.EstimatedDays.Value : null;
            return new DeliveryFact(transit, delay);
        }).ToList();
        var delays = deliveries.Where(x => x.DelayDays is not null).Select(x => x.DelayDays!.Value).ToList();
        var weight = purchases.Sum(x => x.TotalWeightKg);
        var shipping = purchases.Sum(x => x.Expenses.Where(e => e.Kind == PurchaseExpenseKind.Shipping).Sum(e => e.AmountUzs));
        var score = new List<(decimal Value, decimal Weight)>();
        if (delays.Count > 0) score.Add((Math.Max(0, 100 - Math.Max(0, delays.Average()) * 5), 70));
        if (intermediary.Rating is not null) score.Add((Math.Clamp(intermediary.Rating.Value * 20, 0, 100), 30));
        return new IntermediaryAnalytics
        {
            IntermediaryId = intermediary.Id,
            IntermediaryName = intermediary.Name,
            PurchaseCount = purchases.Count,
            CompletedCount = completed.Count,
            DeliverySamples = delays.Count,
            TotalUnits = purchases.Sum(x => x.TotalQuantity),
            TotalWeightKg = Math.Round(weight, 2),
            TotalShippingUzs = shipping,
            AverageShippingPerKgUzs = weight == 0 ? 0 : Math.Round(shipping / weight),
            AverageTransitDays = deliveries.Count == 0 ? 0 : Math.Round(deliveries.Average(x => x.TransitDays), 1),
            AverageDelayDays = delays.Count == 0 ? 0 : Math.Round(delays.Average(), 1),
            OnTimePercent = delays.Count == 0 ? 0 : Math.Round(delays.Count(x => x <= 0) * 100m / delays.Count, 1),
            ReliabilityScore = score.Count == 0 ? null : Math.Round(score.Sum(x => x.Value * x.Weight) / score.Sum(x => x.Weight), 0)
        };
    }

    private static bool Matches(Purchase purchase, Intermediary intermediary) => purchase.IntermediaryId == intermediary.Id || (purchase.IntermediaryId is null && purchase.IntermediaryName?.Equals(intermediary.Name, StringComparison.OrdinalIgnoreCase) == true);
    private sealed record DeliveryFact(decimal TransitDays, decimal? DelayDays);
}
