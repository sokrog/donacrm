namespace Dona.Crm.Web.Domain;

public enum PricingBasis { RemainingStock, PurchaseStock, PurchaseEstimate, Layer }
public enum PricingInput { ManualPrice, Markup }
public enum PricingSource { Product, Purchase, Catalog }

public sealed record PricingLayer(Guid Id, Guid VariantId, string Color, string Size, string SourceNumber,
    DateTimeOffset? ReceivedAt, long Sequence, int Quantity, decimal? Value, int Revision, Guid? PurchaseId)
{
    public decimal? UnitCost => Quantity > 0 ? Value / Quantity : null;
}

public sealed record PricingQuote(Guid ProductId, string Name, string Sku, decimal? CurrentPrice,
    PricingBasis Basis, Guid? PurchaseId, Guid? LayerId, int Quantity, decimal KnownValue, int UnknownQuantity,
    bool Preliminary, string Fingerprint, IReadOnlyList<PricingLayer> Layers)
{
    public decimal? UnitCost => Quantity > 0 && UnknownQuantity == 0 ? KnownValue / Quantity : null;
}

public sealed record SellingPriceChangeLine(PricingQuote Quote, decimal NewPrice, PricingInput Input,
    decimal? TargetMarkup, int RoundingStep, decimal? ActualMarkup);

public sealed record SellingPriceChange(Guid Id, DateTimeOffset AppliedAt, PricingSource Source,
    Guid? SourceId, string RequestFingerprint, IReadOnlyList<SellingPriceChangeLine> Lines);

public sealed record PricingRequestLine(PricingQuote Quote, decimal NewPrice, PricingInput Input,
    decimal? TargetMarkup, int RoundingStep);

public sealed record PricingRequest(Guid Id, PricingSource Source, Guid? SourceId, IReadOnlyList<PricingRequestLine> Lines);
