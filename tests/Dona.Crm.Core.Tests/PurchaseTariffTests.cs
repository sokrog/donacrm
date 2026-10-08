using System.Text.Json;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;

namespace Dona.Crm.Core.Tests;

public sealed class PurchaseTariffTests
{
    private static (Purchase Purchase, Intermediary Partner) Setup()
    {
        var partner = new Intermediary { Name = "Карго", CommissionPercent = 5, RatePerKgUsd = 8, MinimumWeightKg = 3 };
        var purchase = new Purchase { IntermediaryId = partner.Id, CurrencyCode = "CNY", RateToUzs = 1700,
            Items = [new() { Quantity = 2, UnitPrice = 100, UnitWeightKg = .5m }],
            Expenses = [new() { Name = "Упаковка", Amount = 10000, CurrencyCode = "UZS" }], IsCostFinalized = true };
        return (purchase, partner);
    }

    [Fact]
    public void Calculates_goods_only_commission_and_minimum_billed_weight_with_separate_rates()
    {
        var (purchase, partner) = Setup();
        Assert.Equal(2, PurchaseTariffCalculator.Apply(purchase, partner, 12500));
        var commission = purchase.Expenses.Single(x => x.Kind == PurchaseExpenseKind.Commission);
        var shipping = purchase.Expenses.Single(x => x.Kind == PurchaseExpenseKind.Shipping);
        Assert.Equal(10, commission.Amount);
        Assert.Equal("CNY", commission.CurrencyCode);
        Assert.Equal(17000, commission.AmountUzs);
        Assert.Equal(24, shipping.Amount);
        Assert.Equal(300000, shipping.AmountUzs);
        Assert.Equal(ExpenseAllocation.Weight, shipping.Allocation);
        Assert.Contains("минимум 3 кг", shipping.TariffCalculation);
        Assert.False(purchase.IsCostFinalized);
        Assert.Equal(purchase.TotalCostUzs, purchase.Items.Sum(purchase.ItemLandedCostUzs));
    }

    [Fact]
    public void Reload_and_changed_tariff_preserve_existing_expenses_and_manual_edits()
    {
        var (purchase, partner) = Setup();
        PurchaseTariffCalculator.Apply(purchase, partner, 12500);
        purchase.Expenses[1].Amount = 7;
        purchase.Expenses[1].Kind = PurchaseExpenseKind.Other;
        purchase.IsCostFinalized = true;
        purchase = JsonSerializer.Deserialize<Purchase>(JsonSerializer.Serialize(purchase))!;
        var before = JsonSerializer.Serialize(purchase);
        partner.CommissionPercent = 20;
        partner.RatePerKgUsd = 50;
        Assert.Equal(0, PurchaseTariffCalculator.Apply(purchase, partner));
        Assert.Equal(before, JsonSerializer.Serialize(purchase));
        purchase.Expenses.RemoveAll(x => x.TariffComponent == PurchaseExpenseKind.Shipping);
        Assert.Equal(1, PurchaseTariffCalculator.Apply(purchase, partner, 13000));
        Assert.Equal(7, purchase.Expenses.Single(x => x.TariffComponent == PurchaseExpenseKind.Commission).Amount);
        Assert.Equal(150, purchase.Expenses.Single(x => x.TariffComponent == PurchaseExpenseKind.Shipping).Amount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Invalid_shipping_does_not_partially_add_commission(bool missingWeight)
    {
        var (purchase, partner) = Setup();
        if (missingWeight) purchase.Items[0].UnitWeightKg = null;
        var before = JsonSerializer.Serialize(purchase);
        Assert.Throws<InvalidOperationException>(() => PurchaseTariffCalculator.Apply(purchase, partner, missingWeight ? 12500 : null));
        Assert.Equal(before, JsonSerializer.Serialize(purchase));
    }

    [Fact]
    public void Uses_actual_weight_above_minimum_and_usd_purchase_rate_as_default()
    {
        var (purchase, partner) = Setup();
        purchase.CurrencyCode = "USD";
        purchase.RateToUzs = 13000;
        purchase.Items[0].UnitWeightKg = 2;
        PurchaseTariffCalculator.Apply(purchase, partner);
        Assert.Equal(32, purchase.Expenses.Single(x => x.Kind == PurchaseExpenseKind.Shipping).Amount);
        Assert.Equal(416000, purchase.Expenses.Single(x => x.Kind == PurchaseExpenseKind.Shipping).AmountUzs);
    }

    [Fact]
    public void Changing_intermediary_requires_explicit_removal_of_old_tariff_expenses()
    {
        var (purchase, partner) = Setup();
        PurchaseTariffCalculator.Apply(purchase, partner, 12500);
        var other = new Intermediary { CommissionPercent = 7 };
        purchase.IntermediaryId = other.Id;
        var before = JsonSerializer.Serialize(purchase);
        Assert.Throws<InvalidOperationException>(() => PurchaseTariffCalculator.Apply(purchase, other));
        Assert.Equal(before, JsonSerializer.Serialize(purchase));
    }
}
