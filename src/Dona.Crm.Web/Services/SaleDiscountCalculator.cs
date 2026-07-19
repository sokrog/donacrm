namespace Dona.Crm.Web.Services;

public static class SaleDiscountCalculator
{
    public static decimal? AmountFromPercent(decimal subtotal, decimal? percent)
    {
        if (percent is null) return null;
        var normalized = NormalizePercent(percent) ?? 0;
        return decimal.Round(Math.Max(0, subtotal) * normalized / 100, 0, MidpointRounding.AwayFromZero);
    }

    public static decimal? PercentFromAmount(decimal subtotal, decimal? amount)
    {
        if (amount is null) return null;
        if (subtotal <= 0) return 0;
        var normalized = NormalizeAmount(subtotal, amount) ?? 0;
        return decimal.Round(normalized * 100 / subtotal, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal? NormalizePercent(decimal? percent) =>
        percent is null ? null : decimal.Round(Math.Clamp(percent.Value, 0, 100), 2, MidpointRounding.AwayFromZero);

    public static decimal? NormalizeAmount(decimal subtotal, decimal? amount) =>
        amount is null ? null : decimal.Round(Math.Clamp(amount.Value, 0, Math.Max(0, subtotal)), 0, MidpointRounding.AwayFromZero);
}
