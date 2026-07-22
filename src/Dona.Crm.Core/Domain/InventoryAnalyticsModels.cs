namespace Dona.Crm.Web.Domain;

public sealed class InventoryAnalyticsReport
{
    public int PhysicalUnits { get; set; }
    public int ReservedUnits { get; set; }
    public int AvailableUnits { get; set; }
    public decimal InventoryCostUzs { get; set; }
    public decimal PotentialRevenueUzs { get; set; }
    public decimal PotentialProfitUzs { get; set; }
    public decimal StaleInventoryCostUzs { get; set; }
    public int LowStockVariants { get; set; }
    public List<InventoryAnalyticsRow> Rows { get; set; } = [];
}

public sealed class InventoryAnalyticsRow
{
    public Guid ProductId { get; set; }
    public Guid ProductVariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int ReservedQuantity { get; set; }
    public int AvailableQuantity { get; set; }
    public decimal UnitCostUzs { get; set; }
    public decimal SellingPriceUzs { get; set; }
    public decimal InventoryCostUzs { get; set; }
    public decimal PotentialRevenueUzs { get; set; }
    public int Sold30Days { get; set; }
    public int Sold60Days { get; set; }
    public int Sold90Days { get; set; }
    public decimal? DaysOfCover { get; set; }
    public DateTimeOffset? LastSaleAt { get; set; }
    public DateTimeOffset? LastReceiptAt { get; set; }
    public int InactiveDays { get; set; }
    public bool IsStale { get; set; }
    public bool IsLowStock { get; set; }
}
