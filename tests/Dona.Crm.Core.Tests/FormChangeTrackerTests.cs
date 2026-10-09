using Dona.Crm.UI.Components;
using Dona.Crm.Web.Domain;

namespace Dona.Crm.Core.Tests;

public class FormChangeTrackerTests
{
    [Fact]
    public void RevertingFieldsAndListsRestoresCleanState()
    {
        var product = new Product { Name = "Платье" };
        var tracker = new FormChangeTracker();
        tracker.Accept(product);
        product.Name = "Юбка";
        Assert.True(tracker.IsChanged(product));
        product.Name = "Платье";
        product.Notes = "";
        Assert.False(tracker.IsChanged(product));
        product.Variants.Add(new ProductVariant { Color = "Белый" });
        Assert.True(tracker.IsChanged(product));
        product.Variants.Clear();
        Assert.False(tracker.IsChanged(product));
    }

    [Fact]
    public void MainPhotoChangesAndRemovalAreTracked()
    {
        var first = new ProductImage { IsMain = true };
        var second = new ProductImage();
        var product = new Product { Images = [first, second] };
        var tracker = new FormChangeTracker();
        tracker.Accept(product);
        first.IsMain = false;
        second.IsMain = true;
        Assert.True(tracker.IsChanged(product));
        first.IsMain = true;
        second.IsMain = false;
        Assert.False(tracker.IsChanged(product));
        product.Images.Remove(second);
        Assert.True(tracker.IsChanged(product));
        tracker.Accept(product);
        Assert.False(tracker.IsChanged(product));
    }

    [Fact]
    public void AdditionalDatesAndSuccessfulSaveUpdateBaseline()
    {
        var purchase = new Purchase();
        var date = new DateTime(2026, 10, 1);
        var tracker = new FormChangeTracker();
        tracker.Accept(purchase, new { date });
        Assert.True(tracker.IsChanged(purchase, new { date = date.AddDays(1) }));
        Assert.False(tracker.IsChanged(purchase, new { date }));
        purchase.Number = "PO-CHANGED";
        Assert.True(tracker.IsChanged(purchase, new { date }));
        tracker.Accept(purchase, new { date });
        Assert.False(tracker.IsChanged(purchase, new { date }));
    }
}
