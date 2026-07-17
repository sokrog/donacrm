using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Web.Services;

public sealed record PurchaseReceiptResult(int AddedUnits, int UpdatedProducts, int SkippedItems);

public sealed class PurchaseReceivingService(ICatalogRepository catalog, ICommerceRepository commerce)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<PurchaseReceiptResult> ReceiveAsync(Purchase purchase, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var added = 0; var updated = 0; var skipped = 0;
            foreach (var item in purchase.Items.Where(x => x.QuantityToStock > 0))
            {
                if (item.ProductId is null) { skipped++; continue; }
                var product = await catalog.GetProductAsync(item.ProductId.Value, cancellationToken);
                if (product is null) { skipped++; continue; }
                var variant = item.ProductVariantId is not null ? product.Variants.FirstOrDefault(x => x.Id == item.ProductVariantId) : null;
                variant ??= product.Variants.FirstOrDefault(x => x.Color.Equals(item.Color, StringComparison.OrdinalIgnoreCase) && x.Size.Equals(item.Size, StringComparison.OrdinalIgnoreCase));
                if (variant is null) { variant = new ProductVariant { Color = item.Color, Size = item.Size }; product.Variants.Add(variant); }
                var delta = item.QuantityToStock;
                variant.Quantity = (variant.Quantity ?? 0) + delta;
                item.ProductVariantId = variant.Id;
                item.StockedQuantity += delta;
                product.PurchasePriceCny = item.UnitPriceCny;
                product.CnyRateUzs = purchase.CnyRateUzs;
                product.AgentCommissionPercent = purchase.AgentCommissionPercent;
                product.DeliveryCostUzs = (item.Quantity ?? 0) == 0 ? 0 : Math.Round((purchase.ItemShippingUzs(item) + purchase.ItemOtherCostsUzs(item)) / item.Quantity!.Value);
                await catalog.UpsertProductAsync(product, cancellationToken);
                added += delta; updated++;
            }
            if (purchase.Items.Count > 0 && purchase.Items.All(x => x.ProductId is null || x.QuantityToStock == 0)) purchase.Status = PurchaseStatus.Received;
            await commerce.UpsertPurchaseAsync(purchase, cancellationToken);
            return new PurchaseReceiptResult(added, updated, skipped);
        }
        finally { _gate.Release(); }
    }
}
