using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Web.Services;

public sealed class ProfitAnalyticsService
{
    public ProfitAnalyticsReport Build(IEnumerable<Sale> saleSource, IEnumerable<Product> productSource, IEnumerable<Purchase> purchaseSource, MarketingData marketing, DateTime? from, DateTime? to)
    {
        var products = productSource.ToDictionary(x => x.Id);
        var purchases = purchaseSource.ToList();
        var start = from?.Date; var end = to?.Date;
        var sales = saleSource.Where(x => x.Status is SaleStatus.Completed or SaleStatus.Returned)
            .Where(x => start is null || x.CreatedAt.ToLocalTime().Date >= start)
            .Where(x => end is null || x.CreatedAt.ToLocalTime().Date <= end).ToList();
        var lines = sales.SelectMany(sale => sale.Items.Select(item => BuildLine(sale, item, products, purchases))).Where(x => x.ProductId is not null).ToList();
        var collectionMap = marketing.Collections.SelectMany(collection => collection.Products.Where(x => x.ProductId is not null).Select(x => (x.ProductId!.Value, collection.Name))).GroupBy(x => x.Value).ToDictionary(x => x.Key, x => x.Select(v => v.Name).Distinct().ToList());
        var outfitMap = marketing.Outfits.SelectMany(outfit => outfit.Products.Where(x => x.ProductId is not null).Select(x => (x.ProductId!.Value, outfit.Name))).GroupBy(x => x.Value).ToDictionary(x => x.Key, x => x.Select(v => v.Name).Distinct().ToList());
        return new ProfitAnalyticsReport
        {
            Suppliers = Group(lines, x => x.SupplierName),
            Intermediaries = Group(lines, x => x.IntermediaryName),
            Collections = GroupExpanded(lines, x => collectionMap.GetValueOrDefault(x.ProductId!.Value) ?? []),
            Outfits = GroupExpanded(lines, x => outfitMap.GetValueOrDefault(x.ProductId!.Value) ?? []),
            Content = marketing.ContentPosts.Where(x => x.Status == ContentStatus.Published).Select(post => ContentRow(post, marketing, lines)).OrderByDescending(x => x.Date).ToList()
        };
    }

    private static ProfitLine BuildLine(Sale sale, SaleItem item, IReadOnlyDictionary<Guid, Product> products, List<Purchase> purchases)
    {
        var sold = item.SoldQuantity > 0 ? item.SoldQuantity : item.Quantity ?? 0;
        var quantity = Math.Max(0, sold - item.ReturnedQuantity);
        var gross = (item.UnitPriceUzs ?? 0) * sold;
        var revenue = sale.SubtotalUzs == 0 ? 0 : sale.NetTotalUzs * gross / sale.SubtotalUzs;
        var cost = (item.UnitCostUzs ?? 0) * Math.Max(0, sold - sale.RestockedQuantity(item.Id));
        var source = item.ProductId is null ? null : FindSource(item.ProductId.Value, sale.CreatedAt, purchases);
        var supplier = source?.SupplierName ?? (item.ProductId is not null && products.TryGetValue(item.ProductId.Value, out var product) ? product.SupplierName : null);
        return new ProfitLine(sale.Id, sale.CreatedAt, item.ProductId, quantity, revenue, cost, string.IsNullOrWhiteSpace(supplier) ? "Без поставщика" : supplier!, string.IsNullOrWhiteSpace(source?.IntermediaryName) ? "Без посредника" : source!.IntermediaryName!);
    }

    private static Purchase? FindSource(Guid productId, DateTimeOffset soldAt, IEnumerable<Purchase> purchases) => purchases
        .Where(x => x.Items.Any(item => item.ProductId == productId))
        .Select(x => new { Purchase = x, ReceiptAt = x.Receipts.Where(r => r.ReceivedAt <= soldAt && r.Lines.Any(line => line.ProductId == productId)).Select(r => (DateTimeOffset?)r.ReceivedAt).Max() })
        .Where(x => x.ReceiptAt is not null).OrderByDescending(x => x.ReceiptAt).Select(x => x.Purchase).FirstOrDefault();

    private static IReadOnlyList<AnalyticsRow> Group(IEnumerable<ProfitLine> lines, Func<ProfitLine, string> key) => lines.GroupBy(key).Select(x => Row(x.Key, x)).OrderByDescending(x => x.RevenueUzs).ToList();
    private static IReadOnlyList<AnalyticsRow> GroupExpanded(IEnumerable<ProfitLine> lines, Func<ProfitLine, IEnumerable<string>> keys) => lines.SelectMany(line => keys(line).Select(key => (key, line))).GroupBy(x => x.key).Select(x => Row(x.Key, x.Select(v => v.line))).OrderByDescending(x => x.RevenueUzs).ToList();
    private static AnalyticsRow Row(string name, IEnumerable<ProfitLine> source) { var lines = source.ToList(); return new AnalyticsRow(name, lines.Sum(x => x.Quantity), lines.Select(x => x.SaleId).Distinct().Count(), lines.Sum(x => x.RevenueUzs), lines.Sum(x => x.CostUzs)); }

    private static ContentEffectivenessRow ContentRow(ContentPost post, MarketingData marketing, List<ProfitLine> allLines)
    {
        var productIds = new HashSet<Guid>();
        if (post.CollectionId is not null) foreach (var product in marketing.Collections.FirstOrDefault(x => x.Id == post.CollectionId)?.Products ?? []) if (product.ProductId is not null) productIds.Add(product.ProductId.Value);
        if (post.OutfitId is not null) foreach (var product in marketing.Outfits.FirstOrDefault(x => x.Id == post.OutfitId)?.Products ?? []) if (product.ProductId is not null) productIds.Add(product.ProductId.Value);
        var published = post.ScheduledAt?.Date ?? post.CreatedAt.ToLocalTime().Date;
        var lines = allLines.Where(x => x.ProductId is not null && productIds.Contains(x.ProductId.Value) && x.CreatedAt.ToLocalTime().Date >= published).ToList();
        var soldProducts = lines.Where(x => x.Quantity > 0).Select(x => x.ProductId!.Value).Distinct().Count();
        return new ContentEffectivenessRow(post.Title, post.Type?.Display() ?? "Без формата", published, productIds.Count, Math.Max(0, productIds.Count - soldProducts), lines.Select(x => x.SaleId).Distinct().Count(), lines.Sum(x => x.Quantity), lines.Sum(x => x.RevenueUzs), lines.Sum(x => x.CostUzs));
    }

    private sealed record ProfitLine(Guid SaleId, DateTimeOffset CreatedAt, Guid? ProductId, int Quantity, decimal RevenueUzs, decimal CostUzs, string SupplierName, string IntermediaryName);
}
