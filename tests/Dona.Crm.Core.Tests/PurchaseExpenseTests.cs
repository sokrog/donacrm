using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dona.Crm.Web.Domain;

namespace Dona.Crm.Core.Tests;

public sealed class PurchaseExpenseTests
{
    [Fact]
    public void Direct_purchase_needs_no_buyer_expenses_or_exchange_rate_for_uzs()
    {
        var purchase = new Purchase { Number = "DIRECT", CurrencyCode = "UZS", Items = [new() { ProductName = "Товар", Quantity = 2, UnitPrice = 12000 }] };
        Assert.True(purchase.HasCompleteCostInputs);
        Assert.Equal(24000, purchase.TotalCostUzs);
        Assert.Equal(12000, purchase.ItemUnitLandedCostUzs(purchase.Items[0]));
        Assert.False(purchase.NeedsWeight);
    }

    [Fact]
    public void China_example_reconciles_and_delivery_uses_its_own_rate()
    {
        var purchase = new Purchase
        {
            Number = "CHINA", CurrencyCode = "USD", RateToUzs = 12076.387234m,
            Items = [new() { Quantity = 2, UnitPrice = 8 }, new() { Quantity = 1, UnitPrice = 203 }],
            Expenses = [
                new() { Name = "Выкуп", Amount = 15.3m, CurrencyCode = "USD", RateUzs = 12076.387234m },
                new() { Name = "Округление", Amount = .7m, CurrencyCode = "USD", RateUzs = 12076.387234m },
                new() { Name = "Доставка", Amount = 75.15m, CurrencyCode = "USD", RateUzs = 13000 }
            ]
        };
        Assert.Equal(2837951m + 976950m, purchase.TotalCostUzs);
        Assert.Equal(purchase.TotalCostUzs, purchase.Items.Sum(purchase.ItemLandedCostUzs));
    }

    [Theory]
    [InlineData(ExpenseAllocation.Value, 25, 75)]
    [InlineData(ExpenseAllocation.Quantity, 50, 50)]
    [InlineData(ExpenseAllocation.Weight, 20, 80)]
    [InlineData(ExpenseAllocation.Item, 100, 0)]
    public void Distributes_by_selected_basis(ExpenseAllocation allocation, decimal first, decimal second)
    {
        var a = new PurchaseItem { Quantity = 2, UnitPrice = 10, UnitWeightKg = 1 };
        var b = new PurchaseItem { Quantity = 2, UnitPrice = 30, UnitWeightKg = 4 };
        var expense = new PurchaseExpense { Name = "Доставка", Amount = 100, Allocation = allocation, PurchaseItemId = a.Id };
        var purchase = new Purchase { CurrencyCode = "UZS", Items = [a, b], Expenses = [expense] };
        Assert.Equal(first, purchase.ItemExpensesUzs(a));
        Assert.Equal(second, purchase.ItemExpensesUzs(b));
    }

    [Fact]
    public void Fractional_allocations_reconcile_exactly()
    {
        var purchase = new Purchase { CurrencyCode = "UZS",
            Items = Enumerable.Range(0, 7).Select(_ => new PurchaseItem { Quantity = 1, UnitPrice = .33m }).ToList(),
            Expenses = [new() { Name = "Расход", Amount = .01m }] };
        Assert.Equal(.01m, purchase.Items.Sum(purchase.ItemExpensesUzs));
        Assert.Equal(purchase.TotalCostUzs, purchase.Items.Sum(purchase.ItemLandedCostUzs));
        Assert.All(purchase.Items, x => Assert.True(purchase.ItemExpensesUzs(x) >= 0));
    }

    [Fact]
    public void Missing_rate_weight_and_deleted_target_are_not_silently_ignored()
    {
        var purchase = new Purchase { Number = "P", CurrencyCode = "UZS", Items = [new() { ProductName = "Товар", Quantity = 1, UnitPrice = 10 }],
            Expenses = [new() { Name = "Доставка", Amount = 10, CurrencyCode = "USD", Allocation = ExpenseAllocation.Weight }] };
        Assert.False(purchase.HasCompleteCostInputs);
        var errors = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(purchase, new(purchase), errors, true));
        Assert.Contains(errors, x => x.ErrorMessage!.Contains("курс"));
        Assert.Contains(errors, x => x.ErrorMessage!.Contains("вес"));
        purchase.Expenses[0].RateUzs = 12000;
        purchase.Expenses[0].Allocation = ExpenseAllocation.Item;
        purchase.Expenses[0].PurchaseItemId = Guid.NewGuid();
        Assert.False(purchase.HasCompleteCostInputs);
    }

    [Fact]
    public void Old_json_with_legacy_costs_is_rejected_instead_of_losing_amounts()
    {
        Assert.Throws<InvalidOperationException>(() => JsonSerializer.Deserialize<Purchase>("""{"RateToUzs":1000,"AgentCommissionPercent":5,"InternationalShippingUzs":100,"Items":[{"Quantity":2,"UnitPrice":10}]}"""));
    }
}
