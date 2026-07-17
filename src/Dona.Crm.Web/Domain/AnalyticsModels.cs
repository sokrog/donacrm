namespace Dona.Crm.Web.Domain;

public sealed class AnalyticsReport
{
    public int Orders { get; init; }
    public int Units { get; init; }
    public decimal RevenueUzs { get; init; }
    public decimal CostUzs { get; init; }
    public decimal ProfitUzs => RevenueUzs - CostUzs;
    public decimal AverageCheckUzs => Orders == 0 ? 0 : RevenueUzs / Orders;
    public decimal MarginPercent => RevenueUzs == 0 ? 0 : Math.Round(ProfitUzs / RevenueUzs * 100, 1);
    public IReadOnlyList<AnalyticsPoint> Daily { get; init; } = [];
    public IReadOnlyList<AnalyticsRow> Products { get; init; } = [];
    public IReadOnlyList<AnalyticsRow> Categories { get; init; } = [];
    public IReadOnlyList<AnalyticsRow> Customers { get; init; } = [];
}

public sealed record AnalyticsPoint(DateTime Date, decimal RevenueUzs, int Orders);
public sealed record AnalyticsRow(string Name, int Quantity, int Orders, decimal RevenueUzs, decimal CostUzs)
{
    public decimal ProfitUzs => RevenueUzs - CostUzs;
}
