using System.ComponentModel.DataAnnotations;

namespace Dona.Crm.Web.Domain;

public enum ProductStatus { InStock, OnOrder, LowStock, Archived }

public sealed class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required(ErrorMessage = "Укажите SKU")] public string Sku { get; set; } = string.Empty;
    [Required(ErrorMessage = "Укажите название")] public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Футболка";
    public string Gender { get; set; } = "Унисекс";
    public string Season { get; set; } = "Всесезон";
    public ProductStatus Status { get; set; } = ProductStatus.InStock;
    public string? SupplierName { get; set; }
    public string? SourceUrl { get; set; }
    public string? ImageUrl { get; set; }
    public string? Notes { get; set; }
    [Range(0, 1_000_000)] public decimal PurchasePriceCny { get; set; }
    [Range(0, 100_000)] public decimal CnyRateUzs { get; set; } = 1_800;
    [Range(0, 100)] public decimal AgentCommissionPercent { get; set; } = 5;
    [Range(0, 1_000_000_000)] public decimal DeliveryCostUzs { get; set; }
    [Range(0, 1_000_000_000)] public decimal SellingPriceUzs { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ProductVariant> Variants { get; set; } = [];
    public decimal CostUzs => Math.Round(PurchasePriceCny * CnyRateUzs * (1 + AgentCommissionPercent / 100) + DeliveryCostUzs);
    public decimal ProfitUzs => SellingPriceUzs - CostUzs;
    public decimal MarkupPercent => CostUzs == 0 ? 0 : Math.Round(ProfitUzs / CostUzs * 100, 1);
    public int Quantity => Variants.Sum(x => x.Quantity);
}

public sealed class ProductVariant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Color { get; set; } = "Черный";
    public string Size { get; set; } = "M";
    [Range(0, 100_000)] public int Quantity { get; set; }
    public int ReservedQuantity { get; set; }
    public int AvailableQuantity => Math.Max(0, Quantity - ReservedQuantity);
}

public static class ProductStatusText
{
    public static string Display(this ProductStatus status) => status switch
    {
        ProductStatus.InStock => "В наличии",
        ProductStatus.OnOrder => "Под заказ",
        ProductStatus.LowStock => "Заканчивается",
        ProductStatus.Archived => "Архив",
        _ => status.ToString()
    };
}
