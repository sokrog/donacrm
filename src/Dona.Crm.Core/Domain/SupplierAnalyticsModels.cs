namespace Dona.Crm.Web.Domain;

public sealed class SupplierAnalytics
{
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public int PurchaseCount { get; set; }
    public int ReceiptCount { get; set; }
    public int OrderedQuantity { get; set; }
    public int ReceivedQuantity { get; set; }
    public int DefectQuantity { get; set; }
    public int StockedQuantity { get; set; }
    public decimal DefectRatePercent { get; set; }
    public decimal CompletenessPercent { get; set; }
    public int DeliverySamples { get; set; }
    public decimal AverageDelayDays { get; set; }
    public decimal OnTimePercent { get; set; }
    public decimal AverageUnitCostUzs { get; set; }
    public decimal? ReliabilityScore { get; set; }
}

public sealed class ProductSupplierComparison
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public Guid? SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public int ReceiptCount { get; set; }
    public int Quantity { get; set; }
    public decimal AverageUnitPrice { get; set; }
    public string CurrencyCode { get; set; } = "CNY";
    public decimal AverageUnitCostUzs { get; set; }
}
