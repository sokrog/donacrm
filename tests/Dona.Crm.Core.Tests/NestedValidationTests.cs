using System.ComponentModel.DataAnnotations;
using Dona.Crm.Web.Domain;

namespace Dona.Crm.Core.Tests;

public sealed class NestedValidationTests
{
    [Fact]
    public void Product_with_negative_variant_quantity_is_invalid()
    {
        var product = ValidProduct();
        product.Variants[0].Quantity = -1;

        var results = Validate(product);

        var error = Assert.Single(results);
        Assert.Contains("Количество должно быть от 0 до 100 000", error.ErrorMessage);
        Assert.Contains(nameof(Product.Variants), error.MemberNames);
    }

    [Fact]
    public void Valid_product_passes()
    {
        Assert.Empty(Validate(ValidProduct()));
    }

    [Fact]
    public void Sale_with_zero_item_quantity_is_invalid()
    {
        var sale = new Sale
        {
            Number = "S-1",
            Items = [new SaleItem { ProductName = "Платье", Quantity = 0, UnitPriceUzs = 100 }]
        };

        var results = Validate(sale);

        var error = Assert.Single(results);
        Assert.Contains(nameof(Sale.Items), error.MemberNames);
    }

    [Fact]
    public void Valid_sale_passes()
    {
        var sale = new Sale
        {
            Number = "S-1",
            Items = [new SaleItem { ProductName = "Платье", Quantity = 2, UnitPriceUzs = 100 }],
            Payments = [new SalePayment { AmountUzs = 50 }]
        };

        Assert.Empty(Validate(sale));
    }

    [Fact]
    public void Sale_with_invalid_payment_is_invalid()
    {
        var sale = new Sale { Number = "S-1", Payments = [new SalePayment { AmountUzs = 0 }] };

        Assert.Contains(nameof(Sale.Payments), Assert.Single(Validate(sale)).MemberNames);
    }

    [Fact]
    public void Purchase_with_invalid_item_is_invalid()
    {
        var purchase = new Purchase
        {
            Number = "P-1",
            Items = [new PurchaseItem { ProductName = "Куртка", Quantity = -5 }]
        };

        Assert.Contains(nameof(Purchase.Items), Assert.Single(Validate(purchase)).MemberNames);
    }

    private static Product ValidProduct() => new()
    {
        Sku = "SKU-1",
        Name = "Платье",
        Variants = [new ProductVariant { Color = "Чёрный", Size = "M", Quantity = 3 }]
    };

    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }
}