using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Storage;
using System.Text.Json;

namespace Dona.Crm.Web.Services;

public sealed class SalesPaymentService(IInventoryStore store, ISalesRepository? sales = null)
{
    public Task<SalePayment> AddAsync(Sale sale, SalePayment payment, CancellationToken cancellationToken = default) => EntityRollback.RunAsync(sale, async () =>
    {
        using var _ = await InventoryLock.AcquireAsync(cancellationToken);
        if (sales is not null && await sales.GetSaleAsync(sale.Id, cancellationToken) is { } saved
            && JsonSerializer.Serialize(saved) != JsonSerializer.Serialize(sale))
            throw new InventoryException("Продажа изменена или содержит несохранённые правки. Откройте её заново перед платежом.");
        if (sale.Payments.FirstOrDefault(x => x.Id == payment.Id) is { } existing) return existing;
        if (sale.Status is null or SaleStatus.Draft) throw new SaleTransitionException("Сначала зарезервируйте заказ.");
        if (payment.Type is null) throw new InvalidOperationException("Выберите тип операции.");
        if (payment.Status is null) throw new InvalidOperationException("Выберите статус платежа.");
        if (payment.Method is null) throw new InvalidOperationException("Выберите способ платежа.");
        if (payment.AmountUzs is null or <= 0) throw new InvalidOperationException("Сумма должна быть больше нуля.");
        var amount = payment.AmountUzs.Value;
        if (payment.Type == PaymentOperationType.Payment && payment.Status != PaymentStatus.Cancelled && amount > sale.BalanceDueUzs) throw new InvalidOperationException($"Осталось оплатить не более {sale.BalanceDueUzs:N0} сум.");
        if (payment.Type == PaymentOperationType.Refund && payment.Status != PaymentStatus.Cancelled)
        {
            if (amount > sale.RefundDueUzs) throw new InvalidOperationException($"По документам возврата осталось вернуть не более {sale.RefundDueUzs:N0} сум.");
            if (amount > sale.PaidUzs) throw new InvalidOperationException($"Фактически получено не более {sale.PaidUzs:N0} сум.");
        }
        payment.Reference = string.IsNullOrWhiteSpace(payment.Reference) ? null : payment.Reference.Trim();
        payment.Notes = string.IsNullOrWhiteSpace(payment.Notes) ? null : payment.Notes.Trim();
        sale.Payments.Add(payment);
        await store.CommitAsync(InventoryCommit.Create(sales: [sale]), cancellationToken);
        return payment;
    });
}
