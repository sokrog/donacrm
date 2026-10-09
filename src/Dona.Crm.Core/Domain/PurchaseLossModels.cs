namespace Dona.Crm.Web.Domain;

public enum LossTreatment { PeriodExpense, RemainingStock }
public enum PurchaseLossKind { Defect, Shortage }
public sealed record PurchaseLossDecision(Guid OperationId, Guid PurchaseItemId, PurchaseLossKind Kind,
    LossTreatment Treatment, decimal Amount, DateTimeOffset At);
public sealed record PurchaseLossInput(Guid PurchaseItemId, PurchaseLossKind Kind, LossTreatment Treatment);
public sealed record PurchaseLossReview(Guid OperationId, string Signature, DateTimeOffset At);

public sealed partial class Purchase
{
    // Old receipts retain their historical costing until an explicit discrepancy document is posted.
    public bool SeparateReceiptDefects { get; set; }
    public DateTimeOffset? ReceivingCompletedAt { get; set; }
    public List<PurchaseLossDecision> LossDecisions { get; set; } = [];
    public List<PurchaseLossReview> LossReviews { get; set; } = [];
}
