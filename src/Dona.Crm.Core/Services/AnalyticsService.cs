using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Services;

public sealed class AnalyticsService
{
    public AnalyticsReport Build(IEnumerable<Sale> source, IEnumerable<Product> products, DateTime? from, DateTime? to,
        IEnumerable<Purchase>? purchases = null, IEnumerable<StockMovement>? movements = null)
    {
        var productList = products.ToList();
        var categories = productList.ToDictionary(x => x.Id, x => string.IsNullOrWhiteSpace(x.Category) ? "Без категории" : x.Category);
        var sales = source.Where(x => x.DeletedAt is null).ToList();
        var facts = SaleFinancialEvents.Build(sales).Where(x => SaleFinancialEvents.InPeriod(x.At, from, to)).ToList();
        var expenses = new List<PeriodExpenseRow>();
        var valuations = (purchases ?? []).SelectMany(x => x.StockValuations.Select(v => (Source: x.Number, Value: v)))
            .Concat(productList.SelectMany(x => x.StockValuations.Select(v => (Source: x.Sku, Value: v))));
        foreach (var (document, value) in valuations.Where(x => SaleFinancialEvents.InPeriod(x.Value.RecognizedAt, from, to)))
            if (value.ExpenseDelta != 0)
                expenses.Add(new(value.RecognizedAt, document, value.Reason switch
                {
                    StockValuationReason.LossCapitalization => "Распределение потерь на остаток",
                    StockValuationReason.ReceiptDefect => "Брак при приёмке",
                    StockValuationReason.Shortage => "Недостача",
                    StockValuationReason.ShortageCompensation => "Исправление компенсации недостачи",
                    StockValuationReason.ReturnRevaluation => "Возврат доначисления",
                    StockValuationReason.InitialValuation => "Первоначальная оценка",
                    _ => "Доначисление себестоимости"
                }, value.ExpenseDelta));
        foreach (var movement in (movements ?? []).Where(x => x.IsInventoryLoss()
            && SaleFinancialEvents.InPeriod(x.CreatedAt, from, to)))
            expenses.Add(new(movement.CreatedAt, movement.SourceNumber, movement.SourceNumber == "Личное изъятие" ? "Личное изъятие" : "Списание со склада", movement.SourceType == "SaleDeletionDefect"
                ? movement.Consumptions.Any(x => x.TotalCost is null) ? null : movement.Consumptions.Sum(x => x.TotalCost)
                : -movement.ValueDelta));
        IReadOnlyList<AnalyticsRow> Group(Func<SaleFinancialEvent, string> key) => facts.GroupBy(key)
            .Select(g => new AnalyticsRow(g.Key, g.Sum(x => x.Quantity), g.Where(x => x.IsSale).Select(x => x.Sale.Id).Distinct().Count(),
                g.Sum(x => x.Revenue), g.Sum(x => x.Cost)))
            .OrderByDescending(x => x.RevenueUzs).ThenBy(x => x.Name).ToList();
        return new()
        {
            Orders = sales.Count(x => (x.Status == SaleStatus.Shipped || x.Status == SaleStatus.Completed || x.Status == SaleStatus.Returned && x.Returns.Count > 0)
                && SaleFinancialEvents.InPeriod(x.ShippedAt ?? x.CompletedAt ?? x.CreatedAt, from, to)),
            Units = facts.Sum(x => x.Quantity), RevenueUzs = facts.Sum(x => x.Revenue), CostUzs = facts.Sum(x => x.Cost),
            PeriodExpensesUzs = expenses.Sum(x => x.Amount ?? 0), HasUnknownCost = facts.Any(x => x.UnknownCost) || expenses.Any(x => x.Amount is null),
            Expenses = expenses.OrderBy(x => x.At).ToList(),
            Daily = facts.GroupBy(x => x.At.ToLocalTime().Date).OrderBy(x => x.Key)
                .Select(g => new AnalyticsPoint(g.Key, g.Sum(x => x.Revenue), g.Where(x => x.IsSale).Select(x => x.Sale.Id).Distinct().Count())).ToList(),
            Products = Group(x => string.IsNullOrWhiteSpace(x.Item.ProductName) ? "Без названия" : x.Item.ProductName),
            Categories = Group(x => x.Item.ProductId is { } id ? categories.GetValueOrDefault(id, "Без категории") : "Без категории"),
            Customers = Group(x => x.Sale.CustomerName ?? "Без имени")
        };
    }
}
