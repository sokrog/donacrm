namespace Dona.Crm.Web.Domain;

public enum StockMovementType
{
    PurchaseReceipt,
    Reservation,
    ReservationRelease,
    Sale,
    Return,
    Adjustment
}

public sealed class StockMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public StockMovementType Type { get; set; }
    public Guid ProductId { get; set; }
    public Guid ProductVariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public int QuantityDelta { get; set; }
    public int ReservedDelta { get; set; }
    public List<LayerConsumption> Consumptions { get; set; } = [];
    public List<LayerReturnAllocation> ReturnAllocations { get; set; } = [];
    // Legacy movements have no valuation. Zero must be assigned explicitly for reservations.
    public decimal? ValueDelta { get; set; }
    public decimal KnownValueDelta { get; set; }
    public int UnvaluedQuantity { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public Guid? SourceId { get; set; }
    public string SourceNumber { get; set; } = string.Empty;
    public string? Note { get; set; }
}

public static class StockMovementText
{
    public static string Display(this StockMovementType value) => value switch
    {
        StockMovementType.PurchaseReceipt => "Приход",
        StockMovementType.Reservation => "Резерв",
        StockMovementType.ReservationRelease => "Снятие резерва",
        StockMovementType.Sale => "Продажа",
        StockMovementType.Return => "Возврат",
        StockMovementType.Adjustment => "Корректировка",
        _ => value.ToString()
    };
}
