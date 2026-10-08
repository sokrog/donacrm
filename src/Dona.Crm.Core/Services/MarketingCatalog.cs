using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Services;

public static class MarketingCatalog
{
    public static string ProductLabel(Guid? id, string savedName, IReadOnlyList<Product> products)
    {
        var product = products.FirstOrDefault(x => x.Id == id);
        return product is null ? $"{savedName} — товар недоступен, сохранённое название"
            : product.Status == ProductStatus.Archived ? $"{product.Name} — в архиве" : product.Name;
    }

    public static decimal? Price(Guid? id, IReadOnlyList<Product> products) =>
        products.FirstOrDefault(x => x.Id == id)?.SellingPriceUzs;

    public static decimal? OutfitPrice(Outfit outfit, IReadOnlyList<Product> products)
    {
        var prices = outfit.Products.Select(x => Price(x.ProductId, products)).ToList();
        return prices.Any(x => x is null) ? null : prices.Sum(x => x!.Value);
    }
}
