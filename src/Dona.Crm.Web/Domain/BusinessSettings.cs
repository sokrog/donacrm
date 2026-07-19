namespace Dona.Crm.Web.Domain;

public sealed class BusinessSettings
{
    public bool SimpleInterfaceMode { get; set; }
    public bool AutoUpdateStockStatus { get; set; } = true;
    public int LowStockThreshold { get; set; } = 3;
    public bool CountReservedAsUnavailable { get; set; } = true;
    public ProductStatus ZeroStockStatus { get; set; } = ProductStatus.OutOfStock;
    public int PurchaseDueSoonDays { get; set; } = 3;
    public int ContentPlanningHorizonDays { get; set; } = 7;
    public int DefaultAnalyticsPeriodDays { get; set; } = 30;
    public int StaleInventoryDays { get; set; } = 60;
    public string SaleNumberPrefix { get; set; } = "SALE";
    public bool UseGoogleDriveImages { get; set; }
    public string? GoogleDriveFolderId { get; set; }
}
