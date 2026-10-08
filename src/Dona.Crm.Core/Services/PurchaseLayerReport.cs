using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Services;

public sealed record PurchaseLayerRow(Guid LayerId, string Product, string Variant, DateTimeOffset? ReceivedAt,
    int Received, int Sold, int Restocked, int DefectiveReturns, int WrittenOff, int Remaining,
    decimal? CurrentReceiptValue, decimal? RemainingValue, decimal Revenue, decimal KnownNetCost,
    decimal KnownExpenses, bool UnknownCost, bool ApproximateRevenue)
{
    public int NetSold => Sold - Restocked - DefectiveReturns;
    public int QuantityDifference => Remaining - (Received - Sold + Restocked - WrittenOff);
    public decimal? ValueDifference => UnknownCost ? null : CurrentReceiptValue - RemainingValue - KnownNetCost - KnownExpenses;
    public decimal? Profit => UnknownCost || ApproximateRevenue ? null : Revenue - KnownNetCost - KnownExpenses;
}

public sealed record PurchasePendingRow(string Product, int Quantity, decimal? Value);
public sealed record PurchaseLayerReport(IReadOnlyList<PurchaseLayerRow> Layers, IReadOnlyList<PurchasePendingRow> Pending,
    decimal KnownUnlayeredLoss, bool UnknownUnlayeredLoss)
{
    public static PurchaseLayerReport Build(Purchase purchase, IEnumerable<Product> products,
        IEnumerable<Sale> sales, IEnumerable<StockMovement> movements)
    {
        var productList = products.ToList();
        var saleList = sales.ToList();
        var facts = SaleFinancialEvents.Build(saleList);
        var adjustments = movements.Where(x => x.Type == StockMovementType.Adjustment && x.QuantityDelta < 0)
            .SelectMany(x => x.Consumptions).ToList();
        var returns = saleList.Where(x => x.Status is SaleStatus.Completed or SaleStatus.Returned)
            .SelectMany(x => x.Returns).SelectMany(x => x.Items).ToList();
        var valuations = purchase.StockValuations.Concat(productList.SelectMany(x => x.StockValuations)).ToList();
        var rows = new List<PurchaseLayerRow>();
        foreach (var product in productList)
        foreach (var variant in product.Variants)
        foreach (var layer in variant.Layers.Where(x => x.PurchaseId == purchase.Id))
        {
            var layerFacts = facts.Where(x => x.LayerId == layer.Id).ToList();
            var losses = adjustments.Where(x => x.LayerId == layer.Id).ToList();
            var values = valuations.Where(x => x.LayerId == layer.Id).ToList();
            int Returned(ReturnDisposition disposition) => returns.Where(x => x.Disposition == disposition)
                .SelectMany(x => x.LayerAllocations).Where(x => x.LayerId == layer.Id).Sum(x => x.Quantity);
            var affectedSales = layerFacts.Select(x => x.Sale.Id).ToHashSet();
            // A money-only refund has no exact layer allocation. Do not present an exact batch profit.
            var approximate = facts.Any(x => x.LayerId is null && !x.IsSale && x.Revenue != 0 && affectedSales.Contains(x.Sale.Id));
            rows.Add(new(layer.Id, product.Name, string.Join(" · ", new[] { variant.Color, variant.Size }.Where(x => !string.IsNullOrWhiteSpace(x))),
                layer.ReceivedAt, layer.InitialQuantity, layerFacts.Where(x => x.IsSale).Sum(x => x.Quantity),
                Returned(ReturnDisposition.Restock), Returned(ReturnDisposition.Defect), losses.Sum(x => x.Quantity), layer.RemainingQuantity,
                layer.InitialValue, layer.RemainingValue, layerFacts.Sum(x => x.Revenue), layerFacts.Sum(x => x.Cost),
                losses.Sum(x => x.TotalCost ?? 0) + values.Sum(x => x.ExpenseDelta ?? 0),
                layer.InitialValue is null || layer.RemainingValue is null || layerFacts.Any(x => x.UnknownCost)
                    || losses.Any(x => x.TotalCost is null) || values.Any(x => x.ExpenseDelta is null), approximate));
        }
        var pending = purchase.ClosedAt is not null || purchase.Status == PurchaseStatus.Cancelled ? []
            : purchase.Items.Where(x => x.MissingQuantity > 0).Select(x => new PurchasePendingRow(x.ProductName, x.MissingQuantity,
                FifoCostCalculator.Allocate(purchase.HasCompleteCostInputs ? purchase.ItemLandedCostUzs(x) : null,
                    x.Quantity!.Value, x.ReceivedQuantity ?? 0, x.MissingQuantity))).ToList();
        var unlayered = purchase.StockValuations.Where(x => x.LayerId is null).ToList();
        return new(rows.OrderBy(x => x.ReceivedAt).ThenBy(x => x.LayerId).ToList(), pending,
            unlayered.Sum(x => x.ExpenseDelta ?? 0), unlayered.Any(x => x.ExpenseDelta is null));
    }
}
