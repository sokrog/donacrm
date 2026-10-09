using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;

namespace Dona.Crm.Core.Tests;

public sealed class PurchaseAvailabilityTests
{
    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, true, 0)]
    public void Settled_shortage_is_not_an_incoming_delivery(bool receivingCompleted, bool closed, int expected)
    {
        var now = DateTimeOffset.UtcNow;
        var product = new Product();
        var purchase = new Purchase
        {
            Status = PurchaseStatus.PartiallyReceived,
            ReceivingCompletedAt = receivingCompleted ? now : null,
            ClosedAt = closed ? now : null,
            EstimatedDeliveryDate = now.LocalDateTime.Date.AddDays(-1),
            Items = [new() { ProductId = product.Id, Quantity = 5, ReceivedQuantity = 4 }]
        };

        Assert.Equal(expected, PurchaseAvailability.Expected(product.Id, [purchase]));
        var dashboard = HomeDashboardService.Calculate([product], [purchase], [], new(), [], now);
        Assert.Equal(expected, dashboard.IncomingQuantity);
        Assert.Equal(expected, dashboard.DuePurchases.Count);
        Assert.Equal(1, purchase.Items[0].MissingQuantity);
    }

    [Fact]
    public void Fully_received_purchase_does_not_raise_delivery_alert_even_with_stale_status()
    {
        var now = DateTimeOffset.UtcNow;
        var purchase = new Purchase { Status = PurchaseStatus.PartiallyReceived,
            EstimatedDeliveryDate = now.LocalDateTime.Date.AddDays(-1),
            Items = [new() { Quantity = 5, ReceivedQuantity = 5, DefectQuantity = 1 }] };
        Assert.False(PurchaseAvailability.IsAwaitingDelivery(purchase));
        Assert.Empty(HomeDashboardService.Calculate([], [purchase], [], new(), [], now).DuePurchases);
    }
}
