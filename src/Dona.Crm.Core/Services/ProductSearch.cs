using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Services;

public static class ProductSearch
{
    public static IReadOnlyList<Product> Filter(IEnumerable<Product> products, string? query, int limit = 8)
    {
        var value = query?.Trim();
        var source = string.IsNullOrWhiteSpace(value)
            ? products
            : products.Where(x => x.Sku.Contains(value, StringComparison.OrdinalIgnoreCase) || x.Name.Contains(value, StringComparison.OrdinalIgnoreCase));
        return source.OrderBy(x => x.Name).Take(Math.Max(1, limit)).ToList();
    }
}
