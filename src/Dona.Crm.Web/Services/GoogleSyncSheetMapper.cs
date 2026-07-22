using System.Globalization;
using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Services;

public sealed record GoogleSyncSheet(string Title, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<object>> Rows);

public static class GoogleSyncSheetMapper
{
    public static IReadOnlyList<GoogleSyncSheet> Map(DonaSyncSnapshot value)
    {
        var products = value.Products;
        var purchases = value.Purchases;
        var sales = value.Sales;
        var marketing = value.Marketing;
        var settings = value.BusinessSettings;
        var exchangeRates = value.PurchaseHistory.ExchangeRates
            .Select(x => Row(x.Id, D(x.RecordedAt), x.Currency, x.RateUzs, x.PurchaseId, x.ReceiptId, x.PurchaseNumber, x.SupplierName))
            .ToList();
        return
        [
            Sheet("Products", ["Id","Sku","Name","Category","Gender","Season","Status","SupplierName","SourceUrl","ImageUrl","Notes","PurchasePriceCny","CnyRateUzs","AgentCommissionPercent","DeliveryCostUzs","SellingPriceUzs","CreatedAt","PurchaseCurrencyCode"], products.Select(x => Row(x.Id,x.Sku,x.Name,x.Category,x.Gender,x.Season,E(x.Status),S(x.SupplierName),S(x.SourceUrl),S(x.ImageUrl),S(x.Notes),O(x.PurchasePriceCny),O(x.CnyRateUzs),O(x.AgentCommissionPercent),O(x.DeliveryCostUzs),O(x.SellingPriceUzs),D(x.CreatedAt),CurrencyCodes.Normalize(x.PurchaseCurrencyCode,"CNY")))),
            Sheet("ProductVariants", ["Id","ProductId","Color","Size","Quantity","ReservedQuantity"], products.SelectMany(p => p.Variants.Select(x => Row(x.Id,p.Id,x.Color,x.Size,O(x.Quantity),x.ReservedQuantity)))),
            Sheet("ProductImages", ["Id","ProductId","FileName","ContentType","SizeBytes","Storage","StorageKey","Url","Caption","SortOrder","IsMain","CreatedAt"], products.SelectMany(p => p.Images.Select(x => Row(x.Id,p.Id,x.FileName,x.ContentType,x.SizeBytes,x.Storage,x.StorageKey,x.Url,S(x.Caption),x.SortOrder,x.IsMain,D(x.CreatedAt))))),

            Sheet("Suppliers", ["Id","Name","Platform","StoreUrl","Rating","Moq","Contact","WeChat","Intermediary","Notes","CreatedAt"], value.Suppliers.Select(x => Row(x.Id,x.Name,x.Platform,S(x.StoreUrl),O(x.Rating),O(x.Moq),S(x.Contact),S(x.WeChat),S(x.Intermediary),S(x.Notes),D(x.CreatedAt)))),
            Sheet("Categories", ["Id","Name","SortOrder","IsActive"], value.Categories.Select(x => Row(x.Id,x.Name,O(x.SortOrder),x.IsActive))),
            Sheet("Purchases", ["Id","Number","SupplierId","SupplierName","Status","OrderedAt","CnyRateUzs","AgentCommissionPercent","InternationalShippingUzs","OtherCostsUzs","Notes","TrackingCode","EstimatedDeliveryDate","IntermediaryId","IntermediaryName","CurrencyCode"], purchases.Select(x => Row(x.Id,x.Number,O(x.SupplierId),S(x.SupplierName),E(x.Status),D(x.OrderedAt),O(x.CnyRateUzs),O(x.AgentCommissionPercent),O(x.InternationalShippingUzs),O(x.OtherCostsUzs),S(x.Notes),S(x.TrackingCode),x.EstimatedDeliveryDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)??"",O(x.IntermediaryId),S(x.IntermediaryName),CurrencyCodes.Normalize(x.CurrencyCode,"CNY")))),
            Sheet("PurchaseItems", ["Id","PurchaseId","ProductId","ProductName","Quantity","UnitPriceCny","UnitWeightKg","ProductVariantId","Color","Size","ReceivedQuantity","DefectQuantity","StockedQuantity"], purchases.SelectMany(p => p.Items.Select(x => Row(x.Id,p.Id,O(x.ProductId),x.ProductName,O(x.Quantity),O(x.UnitPriceCny),O(x.UnitWeightKg),O(x.ProductVariantId),x.Color,x.Size,O(x.ReceivedQuantity),O(x.DefectQuantity),x.StockedQuantity)))),
            Sheet("PurchaseReceipts", ["Id","PurchaseId","ReceivedAt","Note"], purchases.SelectMany(p => p.Receipts.Select(x => Row(x.Id,p.Id,D(x.ReceivedAt),S(x.Note))))),
            Sheet("PurchaseReceiptItems", ["Id","ReceiptId","PurchaseItemId","ProductId","ProductVariantId","ProductName","Color","Size","ReceivedQuantity","DefectQuantity","StockedQuantity"], purchases.SelectMany(p => p.Receipts.SelectMany(r => r.Lines.Select(x => Row(x.Id,r.Id,x.PurchaseItemId,O(x.ProductId),O(x.ProductVariantId),x.ProductName,x.Color,x.Size,x.ReceivedQuantity,x.DefectQuantity,x.StockedQuantity))))),
            Sheet("Intermediaries", ["Id","Name","Company","ChinaWarehouseAddress","ContactName","Telegram","WeChat","Phone","RatePerKgUsd","CommissionPercent","MinimumWeightKg","EstimatedDays","OfficialImport","Notes","CreatedAt","Rating"], value.Intermediaries.Select(x => Row(x.Id,x.Name,S(x.Company),S(x.ChinaWarehouseAddress),S(x.ContactName),S(x.Telegram),S(x.WeChat),S(x.Phone),O(x.RatePerKgUsd),O(x.CommissionPercent),O(x.MinimumWeightKg),O(x.EstimatedDays),x.OfficialImport,S(x.Notes),D(x.CreatedAt),O(x.Rating)))),

            Sheet("Customers", ["Id","Name","Phone","Instagram","Telegram","Address","Notes","CreatedAt"], value.Customers.Select(x => Row(x.Id,x.Name,S(x.Phone),S(x.Instagram),S(x.Telegram),S(x.Address),S(x.Notes),D(x.CreatedAt)))),
            Sheet("Sales", ["Id","Number","CustomerId","CustomerName","Status","PaymentMethod","DeliveryMethod","DiscountUzs","DeliveryChargeUzs","Notes","CreatedAt","DiscountPercent"], sales.Select(x => Row(x.Id,x.Number,O(x.CustomerId),S(x.CustomerName),E(x.Status),E(x.PaymentMethod),E(x.DeliveryMethod),O(x.DiscountUzs),O(x.DeliveryChargeUzs),S(x.Notes),D(x.CreatedAt),O(x.DiscountPercent)))),
            Sheet("SaleItems", ["Id","SaleId","ProductId","ProductVariantId","ProductName","Color","Size","Quantity","UnitPriceUzs","UnitCostUzs","ReservedQuantity","SoldQuantity","ReturnedQuantity"], sales.SelectMany(s => s.Items.Select(x => Row(x.Id,s.Id,O(x.ProductId),O(x.ProductVariantId),x.ProductName,x.Color,x.Size,O(x.Quantity),O(x.UnitPriceUzs),O(x.UnitCostUzs),x.ReservedQuantity,x.SoldQuantity,x.ReturnedQuantity)))),
            Sheet("SaleReturns", ["Id","SaleId","CreatedAt","Reason","RefundAmountUzs","Notes"], sales.SelectMany(s => s.Returns.Select(x => Row(x.Id,s.Id,D(x.CreatedAt),x.Reason,O(x.RefundAmountUzs),S(x.Notes))))),
            Sheet("SaleReturnItems", ["Id","ReturnId","SaleItemId","ProductId","ProductVariantId","ProductName","Color","Size","Quantity","Disposition"], sales.SelectMany(s => s.Returns.SelectMany(r => r.Items.Select(x => Row(x.Id,r.Id,x.SaleItemId,O(x.ProductId),O(x.ProductVariantId),x.ProductName,x.Color,x.Size,O(x.Quantity),E(x.Disposition)))))),
            Sheet("Payments", ["Id","SaleId","CreatedAt","Type","Status","Method","AmountUzs","Reference","Notes"], sales.SelectMany(s => s.Payments.Select(x => Row(x.Id,s.Id,D(x.CreatedAt),E(x.Type),E(x.Status),E(x.Method),O(x.AmountUzs),S(x.Reference),S(x.Notes))))),

            Sheet("Collections", ["Id","Name","Description","Season","Style","BudgetLimitUzs","Status","CreatedAt"], marketing.Collections.Select(x => Row(x.Id,x.Name,S(x.Description),S(x.Season),S(x.Style),O(x.BudgetLimitUzs),E(x.Status),D(x.CreatedAt)))),
            Sheet("CollectionProducts", ["Id","CollectionId","ProductId","ProductName","SortOrder"], marketing.Collections.SelectMany(c => c.Products.Select(x => Row(x.Id,c.Id,O(x.ProductId),x.ProductName,x.SortOrder)))),
            Sheet("Outfits", ["Id","Name","Description","Occasion","Status","CreatedAt"], marketing.Outfits.Select(x => Row(x.Id,x.Name,S(x.Description),S(x.Occasion),E(x.Status),D(x.CreatedAt)))),
            Sheet("OutfitProducts", ["Id","OutfitId","ProductId","ProductName","SellingPriceUzs","SortOrder"], marketing.Outfits.SelectMany(o => o.Products.Select(x => Row(x.Id,o.Id,O(x.ProductId),x.ProductName,O(x.SellingPriceUzs),x.SortOrder)))),
            Sheet("ContentPlan", ["Id","Title","Type","Status","ScheduledAt","CollectionId","CollectionName","OutfitId","OutfitName","Caption","PublicationUrl","Notes","CreatedAt"], marketing.ContentPosts.Select(x => Row(x.Id,x.Title,E(x.Type),E(x.Status),x.ScheduledAt?.ToString("O")??"",O(x.CollectionId),S(x.CollectionName),O(x.OutfitId),S(x.OutfitName),S(x.Caption),S(x.PublicationUrl),S(x.Notes),D(x.CreatedAt)))),

            Sheet("AppSettings", ["Key","Value"], [
                Row(nameof(settings.SimpleInterfaceMode),settings.SimpleInterfaceMode), Row(nameof(settings.AutoUpdateStockStatus),settings.AutoUpdateStockStatus), Row(nameof(settings.LowStockThreshold),settings.LowStockThreshold), Row(nameof(settings.CountReservedAsUnavailable),settings.CountReservedAsUnavailable), Row(nameof(settings.ZeroStockStatus),settings.ZeroStockStatus), Row(nameof(settings.PurchaseDueSoonDays),settings.PurchaseDueSoonDays), Row(nameof(settings.ContentPlanningHorizonDays),settings.ContentPlanningHorizonDays), Row(nameof(settings.DefaultAnalyticsPeriodDays),settings.DefaultAnalyticsPeriodDays), Row(nameof(settings.StaleInventoryDays),settings.StaleInventoryDays), Row(nameof(settings.SaleNumberPrefix),settings.SaleNumberPrefix), Row(nameof(settings.MainCurrencyCode),CurrencyCodes.Normalize(settings.MainCurrencyCode)), Row(nameof(settings.UseGoogleDriveImages),settings.UseGoogleDriveImages), Row(nameof(settings.GoogleDriveFolderId),S(settings.GoogleDriveFolderId))]),
            Sheet("StockMovements", ["Id","CreatedAt","Type","ProductId","ProductVariantId","ProductName","Sku","Color","Size","QuantityDelta","ReservedDelta","SourceType","SourceId","SourceNumber","Note"], value.StockMovements.Select(x => Row(x.Id,D(x.CreatedAt),x.Type,x.ProductId,x.ProductVariantId,x.ProductName,x.Sku,x.Color,x.Size,x.QuantityDelta,x.ReservedDelta,x.SourceType,O(x.SourceId),x.SourceNumber,S(x.Note)))),
            Sheet("ProductCostHistory", ["Id","RecordedAt","ProductId","ProductVariantId","ProductName","Sku","Color","Size","PurchaseId","ReceiptId","PurchaseNumber","SupplierId","SupplierName","Quantity","UnitPriceCny","CnyRateUzs","UnitLandedCostUzs","CurrencyCode"], value.PurchaseHistory.ProductCosts.Select(x => Row(x.Id,D(x.RecordedAt),x.ProductId,O(x.ProductVariantId),x.ProductName,x.Sku,x.Color,x.Size,x.PurchaseId,x.ReceiptId,x.PurchaseNumber,O(x.SupplierId),x.SupplierName,x.Quantity,x.UnitPriceCny,x.CnyRateUzs,x.UnitLandedCostUzs,CurrencyCodes.Normalize(x.CurrencyCode,"CNY")))),
            Sheet("ExchangeRateHistory", ["Id","RecordedAt","Currency","RateUzs","PurchaseId","ReceiptId","PurchaseNumber","SupplierName"], exchangeRates)
        ];
    }

    private static GoogleSyncSheet Sheet(string title, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object>> rows) => new(title, headers, rows.ToList());
    private static IReadOnlyList<object> Row(params object[] values) => values.Select(Normalize).ToArray();
    private static object Normalize(object value) => value switch { Guid id => id.ToString(), Enum item => item.ToString(), _ => value };
    private static string D(DateTimeOffset value) => value.ToString("O");
    private static string S(string? value) => value ?? string.Empty;
    private static string E<T>(T? value) where T : struct, Enum => value?.ToString() ?? string.Empty;
    private static object O<T>(T? value) where T : struct => value.HasValue ? Normalize(value.Value) : string.Empty;
}
