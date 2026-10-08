using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Web.Services;

public sealed record InitialStockValuationRequest(Guid ProductId, Guid VariantId, Guid LayerId,
    Guid OperationId, int ExpectedRevision, int ExpectedRemainingQuantity, decimal TotalValue);

public sealed class StockValuationService(ICatalogRepository catalog, IInventoryStore store)
{
    public async Task<StockValuationEvent> ValueAsync(InitialStockValuationRequest request, CancellationToken cancellationToken = default)
    {
        using var gate = await InventoryLock.AcquireAsync(cancellationToken);
        if (request.OperationId == Guid.Empty || request.TotalValue < 0
            || decimal.Round(request.TotalValue, 2) != request.TotalValue)
            throw new InventoryException("Укажите стоимость всей партии: неотрицательную сумму с точностью до двух знаков.");
        var product = await catalog.GetProductAsync(request.ProductId, cancellationToken)
            ?? throw new InventoryException("Товар не найден.");
        var variant = product.Variants.SingleOrDefault(x => x.Id == request.VariantId)
            ?? throw new InventoryException("Вариант не найден.");
        FifoCostCalculator.Validate(variant);
        var layer = variant.Layers.SingleOrDefault(x => x.Id == request.LayerId)
            ?? throw new InventoryException("Партия не найдена.");
        var previous = product.StockValuations.SingleOrDefault(x => x.OperationId == request.OperationId);
        if (previous is not null)
        {
            if (previous.LayerId != layer.Id || previous.NewValue != request.TotalValue
                || previous.Reason != StockValuationReason.InitialValuation)
                throw new InventoryException("Эта операция уже сохранена с другими данными.");
            return previous;
        }
        if (layer.Source is not (StockLayerSource.OpeningBalance or StockLayerSource.InventorySurplus) || layer.PurchaseId is not null)
            throw new InventoryException("Стоимость поступления из закупки изменяется в самой закупке.");
        if (layer.InitialValue is not null)
            throw new InventoryException("Партия уже оценена. Обновите карточку товара.");
        if (layer.ValuationRevision != request.ExpectedRevision || layer.RemainingQuantity != request.ExpectedRemainingQuantity)
            throw new InventoryException("Остаток партии изменился. Обновите страницу и проверьте распределение стоимости.");
        return await EntityRollback.RunAsync(product, async () =>
        {
            var remaining = FifoCostCalculator.Allocate(request.TotalValue, layer.InitialQuantity, 0, layer.RemainingQuantity)!.Value;
            var value = new StockValuationEvent(Guid.NewGuid(), request.OperationId, DateTimeOffset.UtcNow,
                StockValuationReason.InitialValuation, layer.Id, null, layer.ValuationRevision, layer.ValuationRevision + 1,
                null, request.TotalValue, remaining, request.TotalValue - remaining);
            layer.InitialValue = request.TotalValue;
            layer.RemainingValue = remaining;
            layer.ValuationRevision++;
            product.StockValuations.Add(value);
            await store.CommitAsync(InventoryCommit.Create(products: [product]), cancellationToken);
            return value;
        });
    }
}
