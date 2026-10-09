using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.Web.Services;

public sealed class StockAdjustmentService(
    ICatalogRepository catalog,
    IInventoryStore store,
    IBusinessSettingsRepository? settings = null,
    ProductStatusService? statuses = null)
{
    public async Task<StockMovement> AdjustAsync(StockAdjustmentRequest request, CancellationToken cancellationToken = default)
    {
        using var _ = await InventoryLock.AcquireAsync(cancellationToken);
        if (request.ProductId is null || request.ProductVariantId is null) throw new InvalidOperationException("Выберите товар и вариант.");
        if (request.Reason is null || !Enum.IsDefined(request.Reason.Value)) throw new InvalidOperationException("Выберите причину корректировки.");
        if (request.WithdrawQuantity is null && request.NewQuantity is null or < 0) throw new InvalidOperationException("Укажите новый остаток.");
        if (string.IsNullOrWhiteSpace(request.Note)) throw new InvalidOperationException("Укажите причину корректировки.");
        var product = await catalog.GetProductAsync(request.ProductId.Value, cancellationToken) ?? throw new InvalidOperationException("Товар не найден.");
        return await EntityRollback.RunAsync(product, async () =>
        {
        if (!Enum.IsDefined(request.LossTreatment)) throw new InvalidOperationException("Выберите способ учёта стоимости.");
        var variant = product.Variants.FirstOrDefault(x => x.Id == request.ProductVariantId) ?? throw new InvalidOperationException("Вариант товара не найден.");
        if (request.WithdrawQuantity is not null && (request.Reason != StockAdjustmentReason.PersonalUse || request.WithdrawQuantity is <= 0 or > 100_000)) throw new InvalidOperationException("Укажите положительное количество для личного изъятия.");
        var newQuantity = request.WithdrawQuantity is { } take ? (variant.Quantity ?? 0) - take : request.NewQuantity;
        if (newQuantity is null or < 0) throw new InvalidOperationException("Недостаточно товара для изъятия.");
        if (newQuantity < variant.ReservedQuantity) throw new InvalidOperationException($"Новый остаток не может быть меньше резерва ({variant.ReservedQuantity} шт.). Сначала снимите резерв.");
        var delta = newQuantity.Value - (variant.Quantity ?? 0);
        if (delta == 0) throw new InvalidOperationException("Новый остаток совпадает с текущим.");
        if (request.Reason is StockAdjustmentReason.Damage or StockAdjustmentReason.Loss or StockAdjustmentReason.PersonalUse && delta > 0) throw new InvalidOperationException("Списание брака или потери не может увеличивать остаток.");
        if (delta > 0 && request.Reason == StockAdjustmentReason.OpeningBalance && request.UnitCost is null)
            throw new InvalidOperationException("Укажите себестоимость начального остатка. Для бесплатного товара введите 0.");
        StockLayerOperations.PrepareEmpty(variant);
        if (request.UnitCost < 0) throw new InvalidOperationException("Себестоимость не может быть отрицательной.");
        var consumptions = delta < 0 ? FifoCostCalculator.Consume(variant, -delta).ToList() : [];
        if (delta > 0) StockLayerOperations.Add(variant, delta, request.UnitCost * delta,
            request.Reason == StockAdjustmentReason.OpeningBalance ? StockLayerSource.OpeningBalance : StockLayerSource.InventorySurplus,
            DateTimeOffset.UtcNow, request.Reason.Value.Display());
        if (settings is not null && statuses is not null)
            product.Status = statuses.Calculate(product, await settings.GetAsync(cancellationToken));
        var movement = new StockMovement { Type = StockMovementType.Adjustment, ProductId = product.Id, ProductVariantId = variant.Id, ProductName = product.Name, Sku = product.Sku, Color = variant.Color, Size = variant.Size, QuantityDelta = delta, SourceType = "Adjustment", SourceNumber = request.Reason.Value.Display(), Note = request.Note.Trim() };
        movement.Consumptions = consumptions;
        movement.LossTreatment = delta < 0 ? request.LossTreatment : null;
        StockLayerOperations.SetValue(movement, delta < 0 ? FifoCostCalculator.Cost(consumptions)
            : new StockCostSummary(Math.Round((request.UnitCost ?? 0) * delta, 2, MidpointRounding.AwayFromZero), request.UnitCost is null ? delta : 0), Math.Sign(delta));
        if (delta < 0 && request.LossTreatment == LossTreatment.RemainingStock)
        {
            var amount = FifoCostCalculator.Cost(consumptions).TotalValue
                ?? throw new InvalidOperationException("Стоимость списания неизвестна. Сначала оцените партии.");
            LossCapitalization.Apply(variant.Layers, amount, movement.Id, null, product.StockValuations);
        }
        await store.CommitAsync(InventoryCommit.Create(products: [product], movements: [movement]), cancellationToken);
        return movement;
        });
    }
}
