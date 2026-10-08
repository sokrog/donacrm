namespace Dona.Crm.Web.Services;

public static class SellingPriceCalculator
{
    public const decimal MaximumPrice = 1_000_000_000m;
    public static readonly int[] RoundingSteps = [1, 1000, 5000, 10000];

    public static decimal RoundUp(decimal price, int step)
    {
        if (!RoundingSteps.Contains(step)) throw new InvalidOperationException("Выберите шаг округления.");
        return decimal.Ceiling(price / step) * step;
    }

    public static decimal Price(decimal? unitCost, decimal markup, int step)
    {
        if (unitCost is null or <= 0) throw new InvalidOperationException("Для расчёта наценки нужна положительная известная себестоимость.");
        if (markup <= -100 || markup > 100_000) throw new InvalidOperationException("Наценка должна быть больше −100% и не больше 100 000%.");
        var price = RoundUp(unitCost.Value * (1 + markup / 100), step);
        ValidatePrice(price);
        return price;
    }

    public static decimal? Markup(decimal? price, decimal? unitCost) => unitCost is > 0 && price is not null
        ? (price.Value - unitCost.Value) / unitCost.Value * 100 : null;

    public static void ValidatePrice(decimal price)
    {
        if (price <= 0 || price > MaximumPrice || price != decimal.Truncate(price))
            throw new InvalidOperationException("Укажите положительную цену в целых UZS, не больше 1 000 000 000.");
    }
}
