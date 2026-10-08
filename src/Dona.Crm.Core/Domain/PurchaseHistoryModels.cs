namespace Dona.Crm.Web.Domain;

public sealed class ProductCostHistoryEntry
{
    public Guid Id { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public Guid PurchaseId { get; set; }
    public Guid ReceiptId { get; set; }
    public string PurchaseNumber { get; set; } = string.Empty;
    public Guid? SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string CurrencyCode { get; set; } = "CNY";
    public decimal RateToUzs { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? UnitPriceCny { get => null; set { if (value is not null) UnitPrice = value.Value; } }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? CnyRateUzs { get => null; set { if (value is not null) RateToUzs = value.Value; } }
    public decimal UnitLandedCostUzs { get; set; }
}

public sealed class ExchangeRateHistoryEntry
{
    public Guid Id { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public string Currency { get; set; } = "CNY";
    public decimal RateUzs { get; set; }
    public Guid PurchaseId { get; set; }
    public Guid ReceiptId { get; set; }
    public string PurchaseNumber { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
}

public sealed class PurchaseHistoryData
{
    public List<ProductCostHistoryEntry> ProductCosts { get; set; } = [];
    public List<ExchangeRateHistoryEntry> ExchangeRates { get; set; } = [];
}
