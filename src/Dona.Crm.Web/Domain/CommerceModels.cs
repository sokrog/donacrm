using System.ComponentModel.DataAnnotations;

namespace Dona.Crm.Web.Domain;

public sealed class Supplier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required(ErrorMessage = "Укажите название")] public string Name { get; set; } = string.Empty;
    public string Platform { get; set; } = "1688";
    public string? StoreUrl { get; set; }
    [Range(0, 5)] public decimal Rating { get; set; }
    [Range(0, 100_000)] public int Moq { get; set; } = 1;
    public string? Contact { get; set; }
    public string? WeChat { get; set; }
    public string? Intermediary { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required(ErrorMessage = "Укажите название")] public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum PurchaseStatus { Draft, Ordered, ChinaWarehouse, Shipped, Received, Cancelled }

public sealed class Purchase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public string Number { get; set; } = $"PO-{DateTime.Now:yyyyMMdd-HHmm}";
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public PurchaseStatus Status { get; set; } = PurchaseStatus.Draft;
    public DateTimeOffset OrderedAt { get; set; } = DateTimeOffset.UtcNow;
    [Range(0, 100_000)] public decimal CnyRateUzs { get; set; } = 1_800;
    [Range(0, 100)] public decimal AgentCommissionPercent { get; set; } = 5;
    [Range(0, 1_000_000_000)] public decimal InternationalShippingUzs { get; set; }
    [Range(0, 1_000_000_000)] public decimal OtherCostsUzs { get; set; }
    public string? Notes { get; set; }
    public List<PurchaseItem> Items { get; set; } = [];
    public decimal GoodsCostCny => Items.Sum(x => x.UnitPriceCny * x.Quantity);
    public decimal GoodsCostUzs => Math.Round(GoodsCostCny * CnyRateUzs);
    public decimal AgentCommissionUzs => Math.Round(GoodsCostUzs * AgentCommissionPercent / 100);
    public decimal TotalCostUzs => GoodsCostUzs + AgentCommissionUzs + InternationalShippingUzs + OtherCostsUzs;
    public int TotalQuantity => Items.Sum(x => x.Quantity);
}

public sealed class PurchaseItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ProductId { get; set; }
    [Required] public string ProductName { get; set; } = string.Empty;
    [Range(1, 100_000)] public int Quantity { get; set; } = 1;
    [Range(0, 1_000_000)] public decimal UnitPriceCny { get; set; }
    [Range(0, 10_000)] public decimal UnitWeightKg { get; set; }
}

public static class PurchaseStatusText
{
    public static string Display(this PurchaseStatus status) => status switch
    {
        PurchaseStatus.Draft => "Черновик",
        PurchaseStatus.Ordered => "Выкуплено",
        PurchaseStatus.ChinaWarehouse => "На складе в Китае",
        PurchaseStatus.Shipped => "Отправлено",
        PurchaseStatus.Received => "Получено",
        PurchaseStatus.Cancelled => "Отменено",
        _ => status.ToString()
    };
}
