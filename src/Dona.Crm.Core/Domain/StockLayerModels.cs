using System.Text.Json.Serialization;

namespace Dona.Crm.Web.Domain;

public enum StockLayerSource { PurchaseReceipt, OpeningBalance, InventorySurplus, Migration, LegacyReturn }

/// <summary>A physical receipt of one variant. Exhausted layers remain available for returns.</summary>
public sealed class StockLayer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public StockLayerSource Source { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    // Unknown historical dates sort before known receipts; Id breaks ties across devices.
    public long Sequence { get; set; }
    public Guid? PurchaseId { get; set; }
    public Guid? PurchaseItemId { get; set; }
    public Guid? ReceiptId { get; set; }
    public Guid? ReceiptLineId { get; set; }
    public string SourceNumber { get; set; } = string.Empty;
    public bool IsApproximate { get; set; }
    public int? HistoricalReceiptQuantity { get; set; }
    public int InitialQuantity { get; set; }
    public int RemainingQuantity { get; set; }
    public int ValuationRevision { get; set; }
    // Totals are authoritative. A rounded unit price cannot preserve the final cent.
    public decimal CapitalizedLossValue { get; set; }
    public decimal? InitialValue { get; set; }
    public decimal? RemainingValue { get; set; }
    [JsonIgnore] public decimal? UnitCost => InitialQuantity == 0 ? null : InitialValue / InitialQuantity;
}

/// <summary>An immutable snapshot of an issued part of a layer.</summary>
public sealed record LayerConsumption(
    Guid Id,
    Guid LayerId,
    int Quantity,
    decimal? UnitCost,
    decimal? TotalCost,
    int ValuationRevision);

/// <summary>An accepted return claims original consumptions even when it is defective.</summary>
public sealed record LayerReturnAllocation(
    Guid ConsumptionId,
    Guid LayerId,
    int Quantity,
    decimal? OriginalCost);

public enum StockValuationReason { Revaluation, InitialValuation, ReturnRevaluation, ReceiptDefect, Shortage, ShortageCompensation, LossCapitalization }

public sealed record PurchaseCompensationCorrection(Guid Id, Guid SettlementId, DateTimeOffset RecognizedAt,
    decimal PreviousRefund, decimal NewRefund, string Reason);

public sealed record PurchaseLateReceipt(Guid Id, Guid SettlementId, Guid ReceiptId, DateTimeOffset RecognizedAt,
    int Quantity, int Defects, string Reason);

public sealed record StockValuationEvent(
    Guid Id,
    Guid OperationId,
    DateTimeOffset RecognizedAt,
    StockValuationReason Reason,
    Guid? LayerId,
    Guid? PurchaseItemId,
    int PreviousRevision,
    int Revision,
    decimal? PreviousValue,
    decimal? NewValue,
    decimal? InventoryValueDelta,
    decimal? ExpenseDelta);

public sealed record PurchaseShortageSettlement(
    Guid Id,
    Guid PurchaseItemId,
    DateTimeOffset RecognizedAt,
    int Quantity,
    decimal? AllocatedCost,
    decimal SupplierRefund,
    decimal? Loss);

/// <summary>Known value is a subtotal, never a substitute for unknown cost.</summary>
public sealed record StockCostSummary(decimal KnownValue, int UnvaluedQuantity)
{
    public bool IsComplete => UnvaluedQuantity == 0;
    public decimal? TotalValue => IsComplete ? KnownValue : null;
}
