using System.Globalization;
using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Services;

/// <summary>Explicitly creates expense snapshots; never updates existing expenses or the partner.</summary>
public static class PurchaseTariffCalculator
{
    public static int Apply(Purchase purchase, Intermediary intermediary, decimal? usdRateUzs = null)
    {
        if (purchase.IntermediaryId != intermediary.Id || intermediary.IsArchived)
            throw new InvalidOperationException("Выберите действующего посредника закупки.");
        if (purchase.Status == PurchaseStatus.Cancelled)
            throw new InvalidOperationException("Нельзя рассчитать тариф отменённой закупки.");
        if (purchase.Expenses.Any(x => x.TariffIntermediaryId is { } id && id != intermediary.Id))
            throw new InvalidOperationException("В закупке есть расходы по тарифу другого посредника. Проверьте их и удалите заменяемые строки перед новым расчётом.");
        if (intermediary.CommissionPercent is < 0 or > 100 || intermediary.RatePerKgUsd < 0 || intermediary.MinimumWeightKg < 0)
            throw new InvalidOperationException("Тариф должен быть неотрицательным, комиссия — от 0 до 100%.");
        bool Has(PurchaseExpenseKind kind) => purchase.Expenses.Any(x => x.TariffComponent == kind);
        var commission = intermediary.CommissionPercent is > 0 && !Has(PurchaseExpenseKind.Commission);
        var shipping = intermediary.RatePerKgUsd is > 0 && !Has(PurchaseExpenseKind.Shipping);
        if (!commission && !shipping) return 0;
        if (purchase.Items.Count == 0 || purchase.Items.Any(x => x.Quantity is null or <= 0))
            throw new InvalidOperationException("Укажите положительное количество каждой позиции.");
        var additions = new List<PurchaseExpense>();
        if (commission)
        {
            if (!CurrencyCodes.Supported.Contains(purchase.CurrencyCode) || purchase.ValidateCostInputs().Any())
                throw new InvalidOperationException("Для комиссии заполните цены товаров, валюту и курс закупки.");
            var amount = Round(purchase.GoodsCost * intermediary.CommissionPercent!.Value / 100);
            additions.Add(new()
            {
                Name = "Комиссия посредника", Kind = PurchaseExpenseKind.Commission,
                TariffComponent = PurchaseExpenseKind.Commission, TariffIntermediaryId = intermediary.Id,
                Amount = amount, CurrencyCode = purchase.CurrencyCode,
                RateUzs = purchase.CurrencyCode == "UZS" ? 1 : purchase.RateToUzs,
                Allocation = purchase.GoodsCost > 0 ? ExpenseAllocation.Value : ExpenseAllocation.Quantity,
                TariffCalculation = $"{intermediary.Name}: товары {N(purchase.GoodsCost)} {purchase.CurrencyCode} × {N(intermediary.CommissionPercent.Value)}% = {N(amount)} {purchase.CurrencyCode}. Без доставки и других расходов."
            });
        }
        if (shipping)
        {
            if (purchase.Items.Any(x => x.UnitWeightKg is null or <= 0))
                throw new InvalidOperationException("Для доставки заполните положительный вес каждой позиции.");
            var rate = usdRateUzs ?? (purchase.CurrencyCode == "USD" ? purchase.RateToUzs : null);
            if (rate is null or <= 0)
                throw new InvalidOperationException("Укажите положительный курс USD в UZS для доставки.");
            var minimum = intermediary.MinimumWeightKg ?? 0;
            var billedWeight = Math.Max(purchase.TotalWeightKg, minimum);
            var amount = Round(billedWeight * intermediary.RatePerKgUsd!.Value);
            additions.Add(new()
            {
                Name = "Доставка посредника", Kind = PurchaseExpenseKind.Shipping,
                TariffComponent = PurchaseExpenseKind.Shipping, TariffIntermediaryId = intermediary.Id,
                Amount = amount, CurrencyCode = "USD", RateUzs = rate, Allocation = ExpenseAllocation.Weight,
                TariffCalculation = $"{intermediary.Name}: фактический вес {N(purchase.TotalWeightKg)} кг, минимум {N(minimum)} кг; оплачиваемый вес {N(billedWeight)} кг × {N(intermediary.RatePerKgUsd.Value)} USD/кг = {N(amount)} USD. Курс: {N(rate.Value)} UZS за USD."
            });
        }
        // Validate every component before modifying the document.
        if (additions.Any(x => x.Amount > 1_000_000_000))
            throw new InvalidOperationException("Сумма расхода превышает допустимый предел.");
        purchase.Expenses.AddRange(additions);
        purchase.IsCostFinalized = false;
        return additions.Count;
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string N(decimal value) => value.ToString("0.########", CultureInfo.GetCultureInfo("ru-RU"));
}
