using System.ComponentModel.DataAnnotations;

namespace Dona.Crm.Web.Domain;

public enum ExpenseAllocation { Value, Quantity, Weight, Item }

public sealed class PurchaseExpense
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required(ErrorMessage = "Укажите название расхода")]
    public string Name { get; set; } = string.Empty;
    [Required(ErrorMessage = "Укажите сумму расхода")]
    [Range(0, 1_000_000_000)] public decimal? Amount { get; set; }
    public string CurrencyCode { get; set; } = "UZS";
    public decimal? RateUzs { get; set; }
    public ExpenseAllocation Allocation { get; set; }
    public Guid? PurchaseItemId { get; set; }
    public decimal AmountUzs => Math.Round((Amount ?? 0) * (CurrencyCode == "UZS" ? 1 : RateUzs ?? 0), 2);
}

public sealed record PurchaseCostRevision(DateTimeOffset ChangedAt, decimal TotalCostUzs, bool IsFinalized);

public sealed partial class Purchase
{
    public List<PurchaseExpense> Expenses { get; set; } = [];
    public bool IsCostFinalized { get; set; }
    public List<PurchaseCostRevision> CostRevisions { get; set; } = [];
    public bool NeedsWeight => Expenses.Any(x => x.Allocation == ExpenseAllocation.Weight) || InternationalShippingUzs > 0;
    public bool HasCompleteCostInputs => !ValidateCostInputs().Any() && !ValidateExpenses().Any();
    public decimal AdditionalCostsUzs => TotalCostUzs - GoodsCostUzs;
    public decimal ItemExpensesUzs(PurchaseItem item) => Expenses.Sum(x => AllocateExpense(x, item));

    public IEnumerable<ValidationResult> ValidateCostInputs()
    {
        if (Items.Count == 0 || Items.Any(x => x.Quantity is null or <= 0 || x.UnitPriceCny is null or < 0))
            yield return new ValidationResult("Укажите количество и закупочную цену каждой позиции.", [nameof(Items)]);
        if (CurrencyCode != "UZS" && CnyRateUzs is null or <= 0)
            yield return new ValidationResult("Укажите курс валюты закупки в UZS.", [nameof(CnyRateUzs)]);
    }

    private IEnumerable<ValidationResult> ValidateExpenses()
    {
        foreach (var expense in Expenses)
        {
            if (string.IsNullOrWhiteSpace(expense.Name) || expense.Amount is null or < 0)
                yield return new ValidationResult("Укажите название и неотрицательную сумму каждого расхода.", [nameof(Expenses)]);
            if (!CurrencyCodes.Supported.Contains(expense.CurrencyCode) || (expense.CurrencyCode != "UZS" && expense.RateUzs is null or <= 0))
                yield return new ValidationResult($"«{expense.Name}»: укажите валюту и положительный курс в UZS.", [nameof(Expenses)]);
            if (!Enum.IsDefined(expense.Allocation))
                yield return new ValidationResult("Выберите способ распределения расхода.", [nameof(Expenses)]);
            if (expense.Allocation == ExpenseAllocation.Item && !Items.Any(x => x.Id == expense.PurchaseItemId))
                yield return new ValidationResult($"«{expense.Name}»: выберите позицию закупки.", [nameof(Expenses)]);
            if (expense.Allocation == ExpenseAllocation.Weight && Items.Any(x => x.UnitWeightKg is null or <= 0))
                yield return new ValidationResult($"«{expense.Name}»: укажите вес каждой позиции.", [nameof(Items)]);
            if (expense.Allocation == ExpenseAllocation.Value && GoodsCostCny <= 0 && expense.Amount > 0)
                yield return new ValidationResult($"«{expense.Name}»: для товаров с нулевой стоимостью выберите распределение по количеству.", [nameof(Expenses)]);
            if (expense.Amount > 0 && TotalQuantity <= 0)
                yield return new ValidationResult("Добавьте товары для распределения расходов.", [nameof(Items)]);
        }
    }

    public decimal AllocateExpense(PurchaseExpense expense, PurchaseItem item)
    {
        if (!Items.Contains(item)) return 0;
        if (expense.Allocation == ExpenseAllocation.Item)
            return expense.PurchaseItemId == item.Id ? expense.AmountUzs : 0;
        decimal Basis(PurchaseItem x) => expense.Allocation switch
        {
            ExpenseAllocation.Quantity => x.Quantity ?? 0,
            ExpenseAllocation.Weight => (x.UnitWeightKg ?? 0) * (x.Quantity ?? 0),
            _ => (x.UnitPriceCny ?? 0) * (x.Quantity ?? 0)
        };
        var eligible = Items.Where(x => Basis(x) > 0).ToList();
        var total = eligible.Sum(Basis);
        if (total <= 0 || Basis(item) <= 0) return 0;
        // Allocate cumulative rounded amounts so all lines reconcile exactly, including cents.
        var index = eligible.IndexOf(item);
        var previous = eligible.Take(index).Sum(Basis);
        return Math.Round(expense.AmountUzs * (previous + Basis(item)) / total, 2)
            - Math.Round(expense.AmountUzs * previous / total, 2);
    }
}
