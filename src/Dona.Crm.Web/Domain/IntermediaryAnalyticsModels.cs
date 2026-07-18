namespace Dona.Crm.Web.Domain;

public sealed class IntermediaryAnalytics
{
    public Guid IntermediaryId { get; set; }
    public string IntermediaryName { get; set; } = string.Empty;
    public int PurchaseCount { get; set; }
    public int CompletedCount { get; set; }
    public int DeliverySamples { get; set; }
    public int TotalUnits { get; set; }
    public decimal TotalWeightKg { get; set; }
    public decimal TotalShippingUzs { get; set; }
    public decimal AverageShippingPerKgUzs { get; set; }
    public decimal AverageTransitDays { get; set; }
    public decimal AverageDelayDays { get; set; }
    public decimal OnTimePercent { get; set; }
    public decimal? ReliabilityScore { get; set; }
}
