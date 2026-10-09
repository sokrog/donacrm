using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Web.Services;

public sealed class ProductPricingService(ICatalogRepository catalog, ICommerceRepository commerce,
    IPricingRepository history, IInventoryStore store)
{
    public async Task<bool> NeedsReviewAsync(SellingPriceChangeLine line, CancellationToken token = default)
    {
        try
        {
            var product = await catalog.GetProductAsync(line.Quote.ProductId, token);
            if (product is null || product.Status == ProductStatus.Archived) return false;
            var purchase = line.Quote.PurchaseId is { } id ? await commerce.GetPurchaseAsync(id, token) : null;
            var basis = line.Quote.Basis == PricingBasis.PurchaseEstimate && purchase is not null && !CanUsePurchaseEstimate(purchase)
                ? PricingBasis.PurchaseStock : line.Quote.Basis;
            if (basis == PricingBasis.Layer && !product.Variants.SelectMany(v => v.Layers).Any(l => l.Id == line.Quote.LayerId && l.RemainingQuantity > 0)) return false;
            var current = BuildQuote(product, basis, purchase, line.Quote.LayerId);
            return CostNeedsReview(line.Quote, current);
        }
        catch (InvalidOperationException) { return true; }
    }

    // Review a saved selling price by its cost, not the concurrency fingerprint used when applying a quote.
    public static bool CostNeedsReview(PricingQuote previous, PricingQuote current) => current.Quantity > 0
        && (previous.UnitCost != current.UnitCost || (previous.UnknownQuantity > 0) != (current.UnknownQuantity > 0));

    public static bool CanUsePurchaseEstimate(Purchase purchase) => purchase.ClosedAt is null
        && purchase.ReceivingCompletedAt is null && purchase.Receipts.Count == 0
        && purchase.Items.All(item => (item.ReceivedQuantity ?? 0) == 0)
        && purchase.Status is not (PurchaseStatus.Received or PurchaseStatus.PartiallyReceived or PurchaseStatus.Cancelled);

    public static PricingQuote? BuildPurchaseQuote(Product product, Purchase purchase)
    {
        var stock = BuildQuote(product, PricingBasis.PurchaseStock, purchase);
        if (stock.Quantity > 0) return stock;
        return CanUsePurchaseEstimate(purchase) ? BuildQuote(product, PricingBasis.PurchaseEstimate, purchase) : null;
    }

    public async Task<PricingQuote> QuoteAsync(Guid productId, PricingBasis basis, Guid? purchaseId = null,
        Guid? layerId = null, CancellationToken token = default)
    {
        var product = await catalog.GetProductAsync(productId, token) ?? throw new InvalidOperationException("Товар не найден.");
        var purchase = purchaseId is { } id ? await commerce.GetPurchaseAsync(id, token)
            ?? throw new InvalidOperationException("Закупка не найдена.") : null;
        return BuildQuote(product, basis, purchase, layerId);
    }

    public static PricingQuote BuildQuote(Product product, PricingBasis basis, Purchase? purchase = null, Guid? layerId = null)
    {
        if (!Enum.IsDefined(basis)) throw new InvalidOperationException("Неизвестное основание расчёта.");
        if (product.Status == ProductStatus.Archived) throw new InvalidOperationException("Товар находится в архиве.");
        if (basis is PricingBasis.PurchaseStock or PricingBasis.PurchaseEstimate
            && (purchase is null || purchase.Status == PurchaseStatus.Cancelled || !purchase.Items.Any(x => x.ProductId == product.Id)))
            throw new InvalidOperationException("Товар отсутствует в действующей закупке.");
        var allLayers = product.Variants.SelectMany(v => v.Layers.Where(l => l.RemainingQuantity > 0).Select(l =>
            new PricingLayer(l.Id, v.Id, v.Color, v.Size, l.SourceNumber, l.ReceivedAt, l.Sequence,
                l.RemainingQuantity, l.RemainingValue, l.ValuationRevision, l.PurchaseId)))
            .OrderBy(l => l.ReceivedAt).ThenBy(l => l.Sequence).ThenBy(l => l.Id).ToList();
        if (basis == PricingBasis.Layer && (layerId is null || allLayers.All(x => x.Id != layerId)))
            throw new InvalidOperationException("Партия больше не имеет остатка. Обновите расчёт.");
        var layers = allLayers.Where(l => basis != PricingBasis.Layer || l.Id == layerId)
            .Where(l => basis != PricingBasis.PurchaseStock || l.PurchaseId == purchase!.Id).ToList();
        var quantity = layers.Sum(x => x.Quantity);
        var known = layers.Sum(x => x.Value ?? 0);
        var unknown = layers.Where(x => x.Value is null).Sum(x => x.Quantity);
        // Broken or unlayered stock must never acquire an invented zero cost.
        if (basis == PricingBasis.RemainingStock)
        {
            var missing = Math.Max(0, product.Quantity - quantity);
            quantity += missing;
            unknown += missing;
        }
        var preliminary = layers.Any(l => product.Variants.SelectMany(v => v.Layers).Any(x => x.Id == l.Id && x.IsApproximate));
        if (basis == PricingBasis.PurchaseEstimate)
        {
            var items = purchase!.Items.Where(x => x.ProductId == product.Id).ToList();
            quantity = items.Sum(x => x.Quantity ?? 0);
            var complete = purchase.HasCompleteCostInputs;
            known = complete ? items.Sum(purchase.ItemLandedCostUzs) : 0;
            unknown = complete ? 0 : quantity;
            preliminary = !purchase.IsCostFinalized;
            layers = [];
        }
        else if (purchase is not null && basis == PricingBasis.PurchaseStock)
            preliminary |= !purchase.IsCostFinalized;
        var fingerprint = Hash(new
        {
            basis, layerId, Purchase = purchase,
            Stock = product.Variants.OrderBy(x => x.Id).Select(x => new
            {
                x.Id, x.Quantity, x.ReservedQuantity, x.StockLayerVersion,
                Layers = x.Layers.OrderBy(l => l.Id).ToList()
            }).ToList()
        });
        return new(product.Id, product.Name, product.Sku, product.SellingPriceUzs, basis, purchase?.Id, layerId,
            quantity, known, unknown, preliminary, fingerprint, layers);
    }

    public async Task<SellingPriceChange> ApplyAsync(PricingRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Id == Guid.Empty || !Enum.IsDefined(request.Source)) throw new InvalidOperationException("Некорректная операция назначения цен.");
        if (request.Lines.Count == 0 || request.Lines.Select(x => x.Quote.ProductId).Distinct().Count() != request.Lines.Count)
            throw new InvalidOperationException("Выберите товары без повторяющихся строк.");
        var requestFingerprint = Hash(request);
        using var gate = await InventoryLock.AcquireAsync(token);
        if ((await history.GetPriceChangesAsync(token)).FirstOrDefault(x => x.Id == request.Id) is { } existing)
        {
            if (existing.RequestFingerprint != requestFingerprint) throw new InvalidOperationException("Этот номер операции уже использован с другими данными.");
            return existing;
        }
        if (request.Source != PricingSource.Catalog && (request.SourceId is null || request.SourceId == Guid.Empty))
            throw new InvalidOperationException("Источник операции не указан.");
        if (request.Source == PricingSource.Product && (request.Lines.Count != 1 || request.Lines[0].Quote.ProductId != request.SourceId))
            throw new InvalidOperationException("Товар не соответствует источнику операции.");
        if (request.Source == PricingSource.Purchase && (await commerce.GetPurchaseAsync(request.SourceId!.Value, token) is not { } source
            || source.Status == PurchaseStatus.Cancelled || request.Lines.Any(x => !source.Items.Any(i => i.ProductId == x.Quote.ProductId))))
            throw new InvalidOperationException("Товары не соответствуют действующей закупке.");
        var products = new List<Product>();
        var changes = new List<SellingPriceChangeLine>();
        foreach (var line in request.Lines)
        {
            SellingPriceCalculator.ValidatePrice(line.NewPrice);
            if (!Enum.IsDefined(line.Input) || !SellingPriceCalculator.RoundingSteps.Contains(line.RoundingStep))
                throw new InvalidOperationException("Некорректный способ расчёта цены.");
            var product = await catalog.GetProductAsync(line.Quote.ProductId, token) ?? throw new InvalidOperationException("Товар удалён. Обновите расчёт.");
            var purchase = line.Quote.PurchaseId is { } id ? await commerce.GetPurchaseAsync(id, token)
                ?? throw new InvalidOperationException("Закупка удалена. Обновите расчёт.") : null;
            var current = BuildQuote(product, line.Quote.Basis, purchase, line.Quote.LayerId);
            if (current.Fingerprint != line.Quote.Fingerprint || current.CurrentPrice != line.Quote.CurrentPrice)
                throw new InvalidOperationException($"Данные «{product.Name}» изменились. Обновите расчёт; введённые цены сохранены на экране.");
            // Recompute from authoritative data; do not trust cost values supplied by an editor.
            if (line.Input == PricingInput.Markup && (line.TargetMarkup is null
                || SellingPriceCalculator.Price(current.UnitCost, line.TargetMarkup.Value, line.RoundingStep) != line.NewPrice))
                throw new InvalidOperationException("Цена не соответствует наценке. Обновите расчёт.");
            if (product.SellingPriceUzs == line.NewPrice) continue;
            product.SellingPriceUzs = line.NewPrice;
            products.Add(product);
            changes.Add(new(current, line.NewPrice, line.Input, line.Input == PricingInput.Markup ? line.TargetMarkup : null,
                line.RoundingStep, SellingPriceCalculator.Markup(line.NewPrice, current.UnitCost)));
        }
        if (changes.Count == 0) throw new InvalidOperationException("Нет изменённых цен.");
        var operation = new SellingPriceChange(request.Id, DateTimeOffset.UtcNow, request.Source, request.SourceId, requestFingerprint, changes);
        await store.CommitAsync(InventoryCommit.Create(products: products) with { PriceChanges = [operation] }, token);
        return operation;
    }

    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}
