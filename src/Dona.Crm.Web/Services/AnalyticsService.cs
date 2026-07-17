using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Services;

public sealed class AnalyticsService
{
    public AnalyticsReport Build(IEnumerable<Sale> source, IEnumerable<Product> products, DateTime? from, DateTime? to)
    {
        var productCategories = products.ToDictionary(x => x.Id, x => string.IsNullOrWhiteSpace(x.Category) ? "Без категории" : x.Category);
        var start = from?.Date;
        var end = to?.Date;
        var sales = source.Where(x => x.Status == SaleStatus.Completed)
            .Where(x => start is null || x.CreatedAt.ToLocalTime().Date >= start)
            .Where(x => end is null || x.CreatedAt.ToLocalTime().Date <= end)
            .ToList();

        var lines = sales.SelectMany(sale => sale.Items.Select(item =>
        {
            var quantity = item.SoldQuantity > 0 ? item.SoldQuantity : item.Quantity ?? 0;
            var gross = (item.UnitPriceUzs ?? 0) * quantity;
            var revenue = sale.SubtotalUzs == 0 ? 0 : sale.TotalUzs * gross / sale.SubtotalUzs;
            var cost = (item.UnitCostUzs ?? 0) * quantity;
            var category = item.ProductId is not null && productCategories.TryGetValue(item.ProductId.Value, out var value) ? value : "Без категории";
            return new Line(sale.Id, sale.CustomerName ?? "Без имени", item.ProductName, category, quantity, revenue, cost);
        })).ToList();

        return new AnalyticsReport
        {
            Orders = sales.Count,
            Units = lines.Sum(x => x.Quantity),
            RevenueUzs = sales.Sum(x => x.TotalUzs),
            CostUzs = lines.Sum(x => x.CostUzs),
            Daily = sales.GroupBy(x => x.CreatedAt.ToLocalTime().Date).OrderBy(x => x.Key).Select(x => new AnalyticsPoint(x.Key, x.Sum(y => y.TotalUzs), x.Count())).ToList(),
            Products = Group(lines, x => string.IsNullOrWhiteSpace(x.Product) ? "Без названия" : x.Product),
            Categories = Group(lines, x => x.Category),
            Customers = Group(lines, x => x.Customer)
        };
    }

    private static IReadOnlyList<AnalyticsRow> Group(IEnumerable<Line> lines, Func<Line, string> key) => lines.GroupBy(key)
        .Select(x => new AnalyticsRow(x.Key, x.Sum(y => y.Quantity), x.Select(y => y.SaleId).Distinct().Count(), x.Sum(y => y.RevenueUzs), x.Sum(y => y.CostUzs)))
        .OrderByDescending(x => x.RevenueUzs).ThenBy(x => x.Name).ToList();

    private sealed record Line(Guid SaleId, string Customer, string Product, string Category, int Quantity, decimal RevenueUzs, decimal CostUzs);
}
