using System.ComponentModel.DataAnnotations;

namespace Dona.Crm.Web.Domain;

public sealed class Supplier
{
    public bool IsArchived { get; set; }
    public Guid? DefaultIntermediaryId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required(ErrorMessage = "Укажите название")] public string Name { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string? StoreUrl { get; set; }
    [Range(0, 5)] public decimal? Rating { get; set; }
    [Range(0, 100_000)] public int? Moq { get; set; }
    public string? Contact { get; set; }
    public string? WeChat { get; set; }
    public string? Intermediary { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Intermediary
{
    public bool IsArchived { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required(ErrorMessage = "Укажите название")] public string Name { get; set; } = string.Empty;
    public string? Company { get; set; }
    public string? ChinaWarehouseAddress { get; set; }
    public string? ContactName { get; set; }
    public string? Telegram { get; set; }
    public string? WeChat { get; set; }
    public string? Phone { get; set; }
    [Range(0, 1_000_000_000)] public decimal? RatePerKgUsd { get; set; }
    [Range(0, 100)] public decimal? CommissionPercent { get; set; }
    [Range(0, 1_000_000_000)] public decimal? MinimumWeightKg { get; set; }
    public int? EstimatedDays { get; set; }
    [Range(0, 5)] public decimal? Rating { get; set; }
    public bool OfficialImport { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required(ErrorMessage = "Укажите название")] public string Name { get; set; } = string.Empty;
    public int? SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public enum PurchaseStatus { Draft, Ordered, ChinaWarehouse, Shipped, PartiallyReceived, Received, Cancelled }

public sealed partial class Purchase : IValidatableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public string Number { get; set; } = string.Empty;
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public Guid? IntermediaryId { get; set; }
    public string? IntermediaryName { get; set; }
    public PurchaseStatus? Status { get; set; }
    public DateTimeOffset OrderedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? TrackingCode { get; set; }
    public DateTime? EstimatedDeliveryDate { get; set; }
    public string CurrencyCode { get; set; } = "CNY";
    [Range(0, 100_000)] public decimal? RateToUzs { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? CnyRateUzs { get => null; set { if (value is not null) RateToUzs = value; } }
    // Retained only to reject legacy payloads explicitly, never to silently lose their amounts.
    public decimal? AgentCommissionPercent { get => null; set => RejectLegacyCost(value); }
    public decimal? InternationalShippingUzs { get => null; set => RejectLegacyCost(value); }
    public decimal? OtherCostsUzs { get => null; set => RejectLegacyCost(value); }
    private static void RejectLegacyCost(decimal? value)
    {
        if (value is not null and not 0)
            throw new InvalidOperationException("Старые поля расходов закупки не поддерживаются. Используйте список расходов; перенос старых данных требует отдельного преобразования.");
    }
    public string? Notes { get; set; }
    public List<PurchaseItem> Items { get; set; } = [];
    public List<PurchaseReceipt> Receipts { get; set; } = [];
    public List<StockValuationEvent> StockValuations { get; set; } = [];
    public List<PurchaseShortageSettlement> ShortageSettlements { get; set; } = [];
    public List<PurchaseCompensationCorrection> CompensationCorrections { get; set; } = [];
    public List<PurchaseLateReceipt> LateReceipts { get; set; } = [];
    public int UnresolvedShortage(PurchaseShortageSettlement settlement) => settlement.Quantity
        - LateReceipts.Where(x => x.SettlementId == settlement.Id).Sum(x => x.Quantity);
    public decimal CurrentRefund(PurchaseShortageSettlement settlement) =>
        CompensationCorrections.LastOrDefault(x => x.SettlementId == settlement.Id)?.NewRefund ?? settlement.SupplierRefund;
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid? ClosingOperationId { get; set; }
    public decimal GoodsCost => Items.Sum(x => (x.UnitPrice ?? 0) * (x.Quantity ?? 0));
    public decimal GoodsCostUzs => Items.Sum(ItemGoodsCostUzs);
    public decimal TotalCostUzs => GoodsCostUzs + Expenses.Sum(x => x.AmountUzs);
    public int TotalQuantity => Items.Sum(x => x.Quantity ?? 0);
    public decimal TotalWeightKg => Items.Sum(x => (x.UnitWeightKg ?? 0) * (x.Quantity ?? 0));
    public decimal ItemGoodsCostUzs(PurchaseItem item) => Math.Round((item.UnitPrice ?? 0) * (item.Quantity ?? 0) * (CurrencyCode == "UZS" ? 1 : RateToUzs ?? 0), 2);
    public decimal ItemLandedCostUzs(PurchaseItem item) => ItemGoodsCostUzs(item) + ItemExpensesUzs(item);
    public decimal ItemUnitLandedCostUzs(PurchaseItem item) => (item.Quantity ?? 0) == 0 ? 0 : Math.Round(ItemLandedCostUzs(item) / item.Quantity!.Value);
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        NestedValidation.ValidateItems(Items, nameof(Items), "Позиция")
            .Concat(NestedValidation.ValidateItems(Expenses, nameof(Expenses), "Расход"))
            .Concat(ValidateExpenses())
            .Concat(IsCostFinalized ? ValidateCostInputs() : []);

}

public sealed class PurchaseReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Note { get; set; }
    public List<PurchaseReceiptLine> Lines { get; set; } = [];
    public int ReceivedQuantity => Lines.Sum(x => x.ReceivedQuantity);
    public int DefectQuantity => Lines.Sum(x => x.DefectQuantity);
    public int StockedQuantity => Lines.Sum(x => x.StockedQuantity);
}

public sealed class PurchaseReceiptLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PurchaseItemId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public int ReceivedQuantity { get; set; }
    public int DefectQuantity { get; set; }
    public int StockedQuantity { get; set; }
}

public sealed class PurchaseItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    [Required(ErrorMessage = "Укажите название товара")] public string ProductName { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    [Range(1, 100_000, ErrorMessage = "Количество должно быть от 1 до 100 000")] public int? Quantity { get; set; }
    [Range(0, 1_000_000, ErrorMessage = "Цена должна быть от 0 до 1 000 000")] public decimal? UnitPrice { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? UnitPriceCny { get => null; set { if (value is not null) UnitPrice = value; } }
    [Range(0, 10_000, ErrorMessage = "Вес должен быть от 0 до 10 000 кг")] public decimal? UnitWeightKg { get; set; }
    // Editable projections: stored unit values remain the source for costing and tariffs.
    [System.Text.Json.Serialization.JsonIgnore]
    public decimal? TotalPrice
    {
        get => Quantity is > 0 && UnitPrice is { } price ? decimal.Round(price * Quantity.Value, 2, MidpointRounding.AwayFromZero) : null;
        set => UnitPrice = PerUnit(value);
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public decimal? TotalWeightKg
    {
        get => Quantity is > 0 && UnitWeightKg is { } weight ? decimal.Round(weight * Quantity.Value, 6, MidpointRounding.AwayFromZero) : null;
        set => UnitWeightKg = PerUnit(value);
    }

    private decimal? PerUnit(decimal? total)
    {
        if (total is null) return null;
        if (Quantity is not > 0) throw new InvalidOperationException("Сначала укажите положительное количество.");
        return total.Value / Quantity.Value;
    }
    [Range(0, 100_000, ErrorMessage = "Принятое количество должно быть от 0 до 100 000")] public int? ReceivedQuantity { get; set; }
    [Range(0, 100_000, ErrorMessage = "Количество брака должно быть от 0 до 100 000")] public int? DefectQuantity { get; set; }
    public int StockedQuantity { get; set; }
    public int MissingQuantity => Math.Max(0, (Quantity ?? 0) - (ReceivedQuantity ?? 0));
    public int AcceptedQuantity => Math.Max(0, Math.Min(Quantity ?? 0, ReceivedQuantity ?? 0) - (DefectQuantity ?? 0));
    public int QuantityToStock => Math.Max(0, AcceptedQuantity - StockedQuantity);
}

public static class PurchaseStatusText
{
    public static string Display(this PurchaseStatus status) => ((PurchaseStatus?)status).Display();
    public static string Display(this PurchaseStatus? status) => status switch
    {
        PurchaseStatus.Draft => "Черновик",
        PurchaseStatus.Ordered => "Выкуплено",
        PurchaseStatus.ChinaWarehouse => "На складе в Китае",
        PurchaseStatus.Shipped => "Отправлено",
        PurchaseStatus.PartiallyReceived => "Частично получено",
        PurchaseStatus.Received => "Получено",
        PurchaseStatus.Cancelled => "Отменено",
        _ => "Не указан"
    };
}
