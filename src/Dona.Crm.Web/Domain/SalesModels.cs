using System.ComponentModel.DataAnnotations;

namespace Dona.Crm.Web.Domain;

public sealed class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required(ErrorMessage = "Укажите имя")] public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Instagram { get; set; }
    public string? Telegram { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum SaleStatus { Draft, Reserved, Paid, Shipped, Completed, Cancelled, Returned }
public enum PaymentMethod { Cash, Card, Transfer, Click, Payme }
public enum DeliveryMethod { Pickup, Courier, Post }

public sealed class Sale
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required(ErrorMessage = "Укажите номер заказа")] public string Number { get; set; } = string.Empty;
    public Guid? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public SaleStatus? Status { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
    public DeliveryMethod? DeliveryMethod { get; set; }
    public decimal? DiscountUzs { get; set; }
    public decimal? DeliveryChargeUzs { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<SaleItem> Items { get; set; } = [];
    public decimal SubtotalUzs => Items.Sum(x => (x.UnitPriceUzs ?? 0) * (x.Quantity ?? 0));
    public decimal TotalUzs => Math.Max(0, SubtotalUzs - (DiscountUzs ?? 0) + (DeliveryChargeUzs ?? 0));
    public decimal CostUzs => Items.Sum(x => (x.UnitCostUzs ?? 0) * x.SoldQuantity);
    public decimal ProfitUzs => TotalUzs - CostUzs;
    public int TotalQuantity => Items.Sum(x => x.Quantity ?? 0);
}

public sealed class SaleItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    [Range(1, 100_000)] public int? Quantity { get; set; }
    [Range(0, 1_000_000_000)] public decimal? UnitPriceUzs { get; set; }
    public decimal? UnitCostUzs { get; set; }
    public int ReservedQuantity { get; set; }
    public int SoldQuantity { get; set; }
    public int ReturnedQuantity { get; set; }
    public int QuantityToReserve => Math.Max(0, (Quantity ?? 0) - ReservedQuantity - SoldQuantity);
    public int QuantityToReturn => Math.Max(0, SoldQuantity - ReturnedQuantity);
}

public static class SalesText
{
    public static string Display(this SaleStatus? value) => value switch { SaleStatus.Draft => "Черновик", SaleStatus.Reserved => "Зарезервирован", SaleStatus.Paid => "Оплачен", SaleStatus.Shipped => "Отправлен", SaleStatus.Completed => "Завершён", SaleStatus.Cancelled => "Отменён", SaleStatus.Returned => "Возврат", _ => "Не указан" };
    public static string Display(this SaleStatus value) => ((SaleStatus?)value).Display();
    public static string Display(this PaymentMethod value) => value switch { PaymentMethod.Cash => "Наличные", PaymentMethod.Card => "Карта", PaymentMethod.Transfer => "Перевод", PaymentMethod.Click => "Click", PaymentMethod.Payme => "Payme", _ => value.ToString() };
    public static string Display(this DeliveryMethod value) => value switch { DeliveryMethod.Pickup => "Самовывоз", DeliveryMethod.Courier => "Курьер", DeliveryMethod.Post => "Почта", _ => value.ToString() };
}
