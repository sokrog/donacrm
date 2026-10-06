using System.ComponentModel.DataAnnotations;

namespace Dona.Crm.Web.Domain;

public enum ProductStatus { InStock, OnOrder, LowStock, OutOfStock, Archived }
public enum ProductImageStorage { Local, GoogleDrive, External }

public sealed class Product : IValidatableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required(ErrorMessage = "Укажите SKU")] public string Sku { get; set; } = string.Empty;
    [Required(ErrorMessage = "Укажите название")] public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string Season { get; set; } = string.Empty;
    public ProductStatus? Status { get; set; }
    public string? SupplierName { get; set; }
    public string? SourceUrl { get; set; }
    public string? ImageUrl { get; set; }
    public string? Notes { get; set; }
    [Range(0, 1_000_000)] public decimal? PurchasePriceCny { get; set; }
    public string PurchaseCurrencyCode { get; set; } = "CNY";
    public Guid? CostPurchaseId { get; set; }
    [Range(0, 100_000)] public decimal? CnyRateUzs { get; set; }
    [Range(0, 100)] public decimal? AgentCommissionPercent { get; set; }
    [Range(0, 1_000_000_000)] public decimal? DeliveryCostUzs { get; set; }
    [Range(0, 1_000_000_000)] public decimal? SellingPriceUzs { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ProductVariant> Variants { get; set; } = [];
    public List<ProductImage> Images { get; set; } = [];
    public ProductImage? PrimaryImage => Images.OrderByDescending(x => x.IsMain).ThenBy(x => x.SortOrder).FirstOrDefault();
    public string? PrimaryImageUrl => PrimaryImage?.Url ?? ImageUrl;
    public decimal CostUzs => Math.Round((PurchasePriceCny ?? 0) * (CnyRateUzs ?? 0) * (1 + (AgentCommissionPercent ?? 0) / 100) + (DeliveryCostUzs ?? 0));
    public decimal ProfitUzs => (SellingPriceUzs ?? 0) - CostUzs;
    public decimal MarkupPercent => CostUzs == 0 ? 0 : Math.Round(ProfitUzs / CostUzs * 100, 1);
    public int Quantity => Variants.Sum(x => x.Quantity ?? 0);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        NestedValidation.ValidateItems(Variants, nameof(Variants), "Вариант");
}

public sealed class ProductImage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public ProductImageStorage Storage { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public int SortOrder { get; set; }
    public bool IsMain { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ProductVariant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Color { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    [Range(0, 100_000, ErrorMessage = "Количество должно быть от 0 до 100 000")] public int? Quantity { get; set; }
    public int ReservedQuantity { get; set; }
    public int AvailableQuantity => Math.Max(0, (Quantity ?? 0) - ReservedQuantity);
}

public static class ProductStatusText
{
    public static string Display(this ProductStatus status) => ((ProductStatus?)status).Display();
    public static string Display(this ProductStatus? status) => status switch
    {
        ProductStatus.InStock => "В наличии",
        ProductStatus.OnOrder => "Под заказ",
        ProductStatus.LowStock => "Заканчивается",
        ProductStatus.OutOfStock => "Нет в наличии",
        ProductStatus.Archived => "Архив",
        _ => "Не указан"
    };
}
