using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Web.Services;

public sealed class ProfitAnalyticsService
{
    public ProfitAnalyticsReport Build(IEnumerable<Sale> saleSource, IEnumerable<Product> productSource, IEnumerable<Purchase> purchaseSource, MarketingData marketing, DateTime? from, DateTime? to, IEnumerable<StockMovement>? movements = null)
    {
        var products = productSource.ToDictionary(x => x.Id);
        var purchases = purchaseSource.ToList();
        var layers = products.Values.SelectMany(x => x.Variants).SelectMany(x => x.Layers).ToDictionary(x => x.Id);
        var facts = SaleFinancialEvents.Build(saleSource).Where(x => SaleFinancialEvents.InPeriod(x.At, from, to));
        var lines = facts.Select(fact =>
        {
            var purchase = fact.LayerId is { } id && layers.TryGetValue(id, out var layer)
                ? purchases.SingleOrDefault(x => x.Id == layer.PurchaseId)
                : fact.Item.Consumptions.Count == 0 && fact.Item.ProductId is { } productId ? FindSource(productId, fact.Sale.CreatedAt, purchases) : null;
            return new ProfitLine(fact.IsSale ? fact.Sale.Id : Guid.Empty, fact.At, fact.Item.ProductId, fact.Quantity, fact.Revenue, fact.Cost,
                purchase?.SupplierName ?? "Без поставщика", purchase?.IntermediaryName ?? "Без посредника");
        }).Where(x => x.ProductId is not null).ToList();
        foreach (var purchase in purchases)
        foreach (var value in purchase.StockValuations.Where(x => SaleFinancialEvents.InPeriod(x.RecognizedAt, from, to) && x.ExpenseDelta != 0))
            lines.Add(new(Guid.Empty, value.RecognizedAt, purchase.Items.FirstOrDefault(x => x.Id == value.PurchaseItemId)?.ProductId,
                0, 0, value.ExpenseDelta ?? 0, purchase.SupplierName ?? "Без поставщика", purchase.IntermediaryName ?? "Без посредника"));
        foreach (var product in products.Values)
        foreach (var value in product.StockValuations.Where(x => SaleFinancialEvents.InPeriod(x.RecognizedAt, from, to) && x.ExpenseDelta != 0))
        {
            var purchaseId = value.LayerId is { } id && layers.TryGetValue(id, out var layer) ? layer.PurchaseId : null;
            var purchase = purchases.FirstOrDefault(x => x.Id == purchaseId);
            lines.Add(new(Guid.Empty, value.RecognizedAt, product.Id, 0, 0, value.ExpenseDelta ?? 0,
                purchase?.SupplierName ?? "Без поставщика", purchase?.IntermediaryName ?? "Без посредника"));
        }
        foreach (var movement in (movements ?? []).Where(x => x.IsInventoryLoss() && SaleFinancialEvents.InPeriod(x.CreatedAt, from, to)))
        foreach (var part in movement.Consumptions)
        {
            var purchaseId = layers.TryGetValue(part.LayerId, out var layer) ? layer.PurchaseId : null;
            var purchase = purchases.FirstOrDefault(x => x.Id == purchaseId);
            lines.Add(new(Guid.Empty, movement.CreatedAt, movement.ProductId, 0, 0, part.TotalCost ?? 0,
                purchase?.SupplierName ?? "Без поставщика", purchase?.IntermediaryName ?? "Без посредника"));
        }
        var collectionMap = marketing.Collections.SelectMany(collection => collection.Products.Where(x => x.ProductId is not null).Select(x => (x.ProductId!.Value, collection.Name))).GroupBy(x => x.Value).ToDictionary(x => x.Key, x => x.Select(v => v.Name).Distinct().ToList());
        var outfitMap = marketing.Outfits.SelectMany(outfit => outfit.Products.Where(x => x.ProductId is not null).Select(x => (x.ProductId!.Value, outfit.Name))).GroupBy(x => x.Value).ToDictionary(x => x.Key, x => x.Select(v => v.Name).Distinct().ToList());
        return new ProfitAnalyticsReport
        {
            Suppliers = Group(lines, x => x.SupplierName),
            Intermediaries = Group(lines, x => x.IntermediaryName),
            Collections = GroupExpanded(lines, x => x.ProductId is { } id ? collectionMap.GetValueOrDefault(id) ?? [] : []),
            Outfits = GroupExpanded(lines, x => x.ProductId is { } id ? outfitMap.GetValueOrDefault(id) ?? [] : []),
            Content = marketing.ContentPosts.Where(x => x.Status == ContentStatus.Published).Select(post => ContentRow(post, marketing, lines)).OrderByDescending(x => x.Date).ToList()
        };
    }

    private static Purchase? FindSource(Guid productId, DateTimeOffset soldAt, IEnumerable<Purchase> purchases) => purchases
        .Where(x => x.Items.Any(item => item.ProductId == productId))
        .Select(x => new { Purchase = x, ReceiptAt = x.Receipts.Where(r => r.ReceivedAt <= soldAt && r.Lines.Any(line => line.ProductId == productId)).Select(r => (DateTimeOffset?)r.ReceivedAt).Max() })
        .Where(x => x.ReceiptAt is not null).OrderByDescending(x => x.ReceiptAt).Select(x => x.Purchase).FirstOrDefault();

    private static IReadOnlyList<AnalyticsRow> Group(IEnumerable<ProfitLine> lines, Func<ProfitLine, string> key) => lines.GroupBy(key).Select(x => Row(x.Key, x)).OrderByDescending(x => x.RevenueUzs).ToList();
    private static IReadOnlyList<AnalyticsRow> GroupExpanded(IEnumerable<ProfitLine> lines, Func<ProfitLine, IEnumerable<string>> keys) => lines.SelectMany(line => keys(line).Select(key => (key, line))).GroupBy(x => x.key).Select(x => Row(x.Key, x.Select(v => v.line))).OrderByDescending(x => x.RevenueUzs).ToList();
    private static AnalyticsRow Row(string name, IEnumerable<ProfitLine> source) { var lines = source.ToList(); return new AnalyticsRow(name, lines.Sum(x => x.Quantity), lines.Where(x => x.SaleId != Guid.Empty).Select(x => x.SaleId).Distinct().Count(), lines.Sum(x => x.RevenueUzs), lines.Sum(x => x.CostUzs)); }

    private static ContentEffectivenessRow ContentRow(ContentPost post, MarketingData marketing, List<ProfitLine> allLines)
    {
        var productIds = new HashSet<Guid>();
        if (post.CollectionId is not null) foreach (var product in marketing.Collections.FirstOrDefault(x => x.Id == post.CollectionId)?.Products ?? []) if (product.ProductId is not null) productIds.Add(product.ProductId.Value);
        if (post.OutfitId is not null) foreach (var product in marketing.Outfits.FirstOrDefault(x => x.Id == post.OutfitId)?.Products ?? []) if (product.ProductId is not null) productIds.Add(product.ProductId.Value);
        var published = post.ScheduledAt?.Date ?? post.CreatedAt.ToLocalTime().Date;
        var lines = allLines.Where(x => x.ProductId is not null && productIds.Contains(x.ProductId.Value) && x.CreatedAt.ToLocalTime().Date >= published).ToList();
        var soldProducts = lines.Where(x => x.Quantity > 0).Select(x => x.ProductId!.Value).Distinct().Count();
        return new ContentEffectivenessRow(post.Title, post.Type?.Display() ?? "Без формата", published, productIds.Count, Math.Max(0, productIds.Count - soldProducts), lines.Where(x => x.SaleId != Guid.Empty).Select(x => x.SaleId).Distinct().Count(), lines.Sum(x => x.Quantity), lines.Sum(x => x.RevenueUzs), lines.Sum(x => x.CostUzs));
    }

    private sealed record ProfitLine(Guid SaleId, DateTimeOffset CreatedAt, Guid? ProductId, int Quantity, decimal RevenueUzs, decimal CostUzs, string SupplierName, string IntermediaryName);
}
