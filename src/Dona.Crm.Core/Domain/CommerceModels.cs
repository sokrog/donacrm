using System.ComponentModel.DataAnnotations;

namespace Dona.Crm.Web.Domain;

public sealed class Supplier
{
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
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required(ErrorMessage = "Укажите название")] public string Name { get; set; } = string.Empty;
    public string? Company { get; set; }
    public string? ChinaWarehouseAddress { get; set; }
    public string? ContactName { get; set; }
    public string? Telegram { get; set; }
    public string? WeChat { get; set; }
    public string? Phone { get; set; }
    public decimal? RatePerKgUsd { get; set; }
    public decimal? CommissionPercent { get; set; }
    public decimal? MinimumWeightKg { get; set; }
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

public sealed class Purchase
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
    [Range(0, 100_000)] public decimal? CnyRateUzs { get; set; }
    [Range(0, 100)] public decimal? AgentCommissionPercent { get; set; }
    [Range(0, 1_000_000_000)] public decimal? InternationalShippingUzs { get; set; }
    [Range(0, 1_000_000_000)] public decimal? OtherCostsUzs { get; set; }
    public string? Notes { get; set; }
    public List<PurchaseItem> Items { get; set; } = [];
    public List<PurchaseReceipt> Receipts { get; set; } = [];
    public decimal GoodsCostCny => Items.Sum(x => (x.UnitPriceCny ?? 0) * (x.Quantity ?? 0));
    public decimal GoodsCostUzs => Math.Round(GoodsCostCny * (CnyRateUzs ?? 0));
    public decimal AgentCommissionUzs => Math.Round(GoodsCostUzs * (AgentCommissionPercent ?? 0) / 100);
    public decimal TotalCostUzs => GoodsCostUzs + AgentCommissionUzs + (InternationalShippingUzs ?? 0) + (OtherCostsUzs ?? 0);
    public int TotalQuantity => Items.Sum(x => x.Quantity ?? 0);
    public decimal TotalWeightKg => Items.Sum(x => (x.UnitWeightKg ?? 0) * (x.Quantity ?? 0));
    public decimal ItemGoodsCostUzs(PurchaseItem item) => Math.Round((item.UnitPriceCny ?? 0) * (item.Quantity ?? 0) * (CnyRateUzs ?? 0));
    public decimal ItemCommissionUzs(PurchaseItem item) => Allocate(ItemGoodsCostUzs(item), GoodsCostUzs, AgentCommissionUzs, item.Quantity ?? 0);
    public decimal ItemShippingUzs(PurchaseItem item) => Allocate((item.UnitWeightKg ?? 0) * (item.Quantity ?? 0), TotalWeightKg, InternationalShippingUzs ?? 0, item.Quantity ?? 0);
    public decimal ItemOtherCostsUzs(PurchaseItem item) => Allocate(ItemGoodsCostUzs(item), GoodsCostUzs, OtherCostsUzs ?? 0, item.Quantity ?? 0);
    public decimal ItemLandedCostUzs(PurchaseItem item) => ItemGoodsCostUzs(item) + ItemCommissionUzs(item) + ItemShippingUzs(item) + ItemOtherCostsUzs(item);
    public decimal ItemUnitLandedCostUzs(PurchaseItem item) => (item.Quantity ?? 0) == 0 ? 0 : Math.Round(ItemLandedCostUzs(item) / item.Quantity!.Value);
    private decimal Allocate(decimal basis, decimal totalBasis, decimal totalCost, int fallbackQuantity) => totalCost == 0 ? 0 : totalBasis > 0 ? Math.Round(totalCost * basis / totalBasis) : TotalQuantity > 0 ? Math.Round(totalCost * fallbackQuantity / TotalQuantity) : 0;
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
    [Required] public string ProductName { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    [Range(1, 100_000)] public int? Quantity { get; set; }
    [Range(0, 1_000_000)] public decimal? UnitPriceCny { get; set; }
    [Range(0, 10_000)] public decimal? UnitWeightKg { get; set; }
    [Range(0, 100_000)] public int? ReceivedQuantity { get; set; }
    [Range(0, 100_000)] public int? DefectQuantity { get; set; }
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
