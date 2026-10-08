namespace Dona.Crm.Web.Domain;

public sealed class BusinessSettings
{
    public int LowStockThreshold { get; set; } = 3;
    public bool CountReservedAsUnavailable { get; set; } = true;
    public bool PreventSalesBelowCost { get; set; } = true;
    public ProductStatus ZeroStockStatus { get; set; } = ProductStatus.OutOfStock;
    public int PurchaseDueSoonDays { get; set; } = 3;
    public int ContentPlanningHorizonDays { get; set; } = 7;
    public int DefaultAnalyticsPeriodDays { get; set; } = 30;
    public int StaleInventoryDays { get; set; } = 60;
    public string SaleNumberPrefix { get; set; } = "SALE";
    public const string AccountingCurrency = "UZS";
    public string MainCurrencyCode
    {
        get => AccountingCurrency;
        set => EnsureAccountingCurrency(value);
    }

    public static void EnsureAccountingCurrency(string? code)
    {
        if (!string.Equals(code?.Trim(), AccountingCurrency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Валюта учёта — UZS. Данные с другой валютой учёта нельзя загрузить без отдельного преобразования сумм.");
    }
    public bool OnboardingCompleted { get; set; }
    public bool UseGoogleDriveImages { get; set; }
}
