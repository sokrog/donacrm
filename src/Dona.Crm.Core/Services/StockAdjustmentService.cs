using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Web.Services;

public sealed class StockAdjustmentService(
    ICatalogRepository catalog,
    IStockMovementRepository movements,
    IBusinessSettingsRepository? settings = null,
    ProductStatusService? statuses = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<StockMovement> AdjustAsync(StockAdjustmentRequest request, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (request.ProductId is null || request.ProductVariantId is null) throw new InvalidOperationException("Выберите товар и вариант.");
            if (request.Reason is null) throw new InvalidOperationException("Выберите причину корректировки.");
            if (request.NewQuantity is null or < 0) throw new InvalidOperationException("Укажите новый остаток.");
            if (string.IsNullOrWhiteSpace(request.Note)) throw new InvalidOperationException("Укажите причину корректировки.");
            var product = await catalog.GetProductAsync(request.ProductId.Value, cancellationToken) ?? throw new InvalidOperationException("Товар не найден.");
            var variant = product.Variants.FirstOrDefault(x => x.Id == request.ProductVariantId) ?? throw new InvalidOperationException("Вариант товара не найден.");
            if (request.NewQuantity < variant.ReservedQuantity) throw new InvalidOperationException($"Новый остаток не может быть меньше резерва ({variant.ReservedQuantity} шт.). Сначала снимите резерв.");
            var delta = request.NewQuantity.Value - (variant.Quantity ?? 0);
            if (delta == 0) throw new InvalidOperationException("Новый остаток совпадает с текущим.");
            if (request.Reason is StockAdjustmentReason.Damage or StockAdjustmentReason.Loss && delta > 0) throw new InvalidOperationException("Списание брака или потери не может увеличивать остаток.");
            variant.Quantity = request.NewQuantity;
            if (settings is not null && statuses is not null)
                product.Status = statuses.Calculate(product, await settings.GetAsync(cancellationToken));
            await catalog.UpsertProductAsync(product, cancellationToken);
            var movement = new StockMovement { Type = StockMovementType.Adjustment, ProductId = product.Id, ProductVariantId = variant.Id, ProductName = product.Name, Sku = product.Sku, Color = variant.Color, Size = variant.Size, QuantityDelta = delta, SourceType = "Adjustment", SourceNumber = request.Reason.Value.Display(), Note = request.Note.Trim() };
            await movements.AddRangeAsync([movement], cancellationToken);
            return movement;
        }
        finally { _gate.Release(); }
    }
}
