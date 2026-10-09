using System.ComponentModel.DataAnnotations;

namespace Dona.Crm.Web.Domain;

public enum StockAdjustmentReason { OpeningBalance, InventoryCount, Damage, Loss, Other, PersonalUse }

public sealed class StockAdjustmentRequest
{
    [Range(1, 100_000, ErrorMessage = "Укажите количество для изъятия от 1 до 100 000")] public int? WithdrawQuantity { get; set; }
    public LossTreatment LossTreatment { get; set; }
    [Required] public Guid? ProductId { get; set; }
    [Required] public Guid? ProductVariantId { get; set; }
    [Required(ErrorMessage = "Выберите причину корректировки")] public StockAdjustmentReason? Reason { get; set; }
    [Required(ErrorMessage = "Укажите новый остаток"), Range(0, 100_000)] public int? NewQuantity { get; set; }
    [Required(ErrorMessage = "Укажите причину корректировки")] public string Note { get; set; } = string.Empty;
    [Range(0, 1_000_000_000)] public decimal? UnitCost { get; set; }
}

public static class StockAdjustmentText
{
    public static string Display(this StockAdjustmentReason value) => value switch
    {
        StockAdjustmentReason.OpeningBalance => "Начальный остаток",
        StockAdjustmentReason.InventoryCount => "Инвентаризация",
        StockAdjustmentReason.PersonalUse => "Личное изъятие",
        StockAdjustmentReason.Damage => "Списание брака",
        StockAdjustmentReason.Loss => "Потеря",
        StockAdjustmentReason.Other => "Другое",
        _ => value.ToString()
    };
}
