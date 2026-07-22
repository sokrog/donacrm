namespace Dona.Crm.Web.Domain;

public sealed class ProfitAnalyticsReport
{
    public IReadOnlyList<AnalyticsRow> Suppliers { get; init; } = [];
    public IReadOnlyList<AnalyticsRow> Intermediaries { get; init; } = [];
    public IReadOnlyList<AnalyticsRow> Collections { get; init; } = [];
    public IReadOnlyList<AnalyticsRow> Outfits { get; init; } = [];
    public IReadOnlyList<ContentEffectivenessRow> Content { get; init; } = [];
}

public sealed record ContentEffectivenessRow(string Name, string Format, DateTime Date, int Products, int ProductsWithoutSales, int Orders, int Quantity, decimal RevenueUzs, decimal CostUzs)
{
    public decimal ProfitUzs => RevenueUzs - CostUzs;
}
