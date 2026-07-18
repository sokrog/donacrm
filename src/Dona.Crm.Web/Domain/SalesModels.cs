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
public enum ReturnDisposition { Restock, Defect, Rejected }
public enum PaymentOperationType { Payment, Refund }
public enum PaymentStatus { Pending, Completed, Cancelled }

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
    public List<SaleReturn> Returns { get; set; } = [];
    public List<SalePayment> Payments { get; set; } = [];
    public decimal SubtotalUzs => Items.Sum(x => (x.UnitPriceUzs ?? 0) * (x.Quantity ?? 0));
    public decimal TotalUzs => Math.Max(0, SubtotalUzs - (DiscountUzs ?? 0) + (DeliveryChargeUzs ?? 0));
    public decimal RefundedUzs => Returns.Sum(x => x.RefundAmountUzs ?? 0);
    public decimal NetTotalUzs => Math.Max(0, TotalUzs - RefundedUzs);
    public decimal ReceivedPaymentsUzs => Payments.Where(x => x.Status == PaymentStatus.Completed && x.Type == PaymentOperationType.Payment).Sum(x => x.AmountUzs ?? 0);
    public decimal ReturnedPaymentsUzs => Payments.Where(x => x.Status == PaymentStatus.Completed && x.Type == PaymentOperationType.Refund).Sum(x => x.AmountUzs ?? 0);
    public decimal PaidUzs => Math.Max(0, ReceivedPaymentsUzs - ReturnedPaymentsUzs);
    public decimal BalanceDueUzs => Math.Max(0, NetTotalUzs - PaidUzs);
    public decimal RefundDueUzs => Math.Max(0, RefundedUzs - ReturnedPaymentsUzs);
    public int RestockedQuantity(Guid saleItemId) => Returns.SelectMany(x => x.Items).Where(x => x.SaleItemId == saleItemId && x.Disposition == ReturnDisposition.Restock).Sum(x => x.Quantity ?? 0);
    public decimal CostUzs => Items.Sum(x => (x.UnitCostUzs ?? 0) * Math.Max(0, x.SoldQuantity - RestockedQuantity(x.Id)));
    public decimal ProfitUzs => NetTotalUzs - CostUzs;
    public int TotalQuantity => Items.Sum(x => x.Quantity ?? 0);
}

public sealed class SalePayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public PaymentOperationType? Type { get; set; }
    public PaymentStatus? Status { get; set; }
    public PaymentMethod? Method { get; set; }
    [Range(1, 1_000_000_000)] public decimal? AmountUzs { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
}

public sealed class SaleReturn
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [Required(ErrorMessage = "Укажите причину возврата")] public string Reason { get; set; } = string.Empty;
    [Range(0, 1_000_000_000)] public decimal? RefundAmountUzs { get; set; }
    public string? Notes { get; set; }
    public List<SaleReturnItem> Items { get; set; } = [];
    public int AcceptedQuantity => Items.Where(x => x.Disposition is ReturnDisposition.Restock or ReturnDisposition.Defect).Sum(x => x.Quantity ?? 0);
    public int RestockedQuantity => Items.Where(x => x.Disposition == ReturnDisposition.Restock).Sum(x => x.Quantity ?? 0);
    public int DefectQuantity => Items.Where(x => x.Disposition == ReturnDisposition.Defect).Sum(x => x.Quantity ?? 0);
    public int RejectedQuantity => Items.Where(x => x.Disposition == ReturnDisposition.Rejected).Sum(x => x.Quantity ?? 0);
}

public sealed class SaleReturnItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SaleItemId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    [Range(1, 100_000)] public int? Quantity { get; set; }
    public ReturnDisposition? Disposition { get; set; }
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
    public static string Display(this ReturnDisposition value) => value switch { ReturnDisposition.Restock => "Вернуть на склад", ReturnDisposition.Defect => "Списать как брак", ReturnDisposition.Rejected => "Отказать в возврате", _ => value.ToString() };
    public static string Display(this PaymentOperationType value) => value switch { PaymentOperationType.Payment => "Оплата", PaymentOperationType.Refund => "Возврат денег", _ => value.ToString() };
    public static string Display(this PaymentStatus value) => value switch { PaymentStatus.Pending => "Ожидается", PaymentStatus.Completed => "Проведён", PaymentStatus.Cancelled => "Отменён", _ => value.ToString() };
}
