using Dona.Crm.Web.Components;
using Dona.Crm.Web.Storage;
using Dona.Crm.Web.Services;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "data-protection")));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<GoogleSheetsOptions>(builder.Configuration.GetSection(GoogleSheetsOptions.SectionName));
builder.Services.AddSingleton<GoogleSheetsSettingsStore>();
builder.Services.AddScoped<LoadingState>();
builder.Services.AddScoped<InterfaceModeState>();
builder.Services.AddScoped<JsonCatalogRepository>();
builder.Services.AddScoped<GoogleSheetsCatalogRepository>();
builder.Services.AddScoped<SwitchingCatalogRepository>();
builder.Services.AddScoped<CatalogMigrationService>();
builder.Services.AddScoped<ICatalogRepository>(services => services.GetRequiredService<SwitchingCatalogRepository>());
builder.Services.AddScoped<JsonCommerceRepository>();
builder.Services.AddScoped<GoogleSheetsCommerceRepository>();
builder.Services.AddScoped<SwitchingCommerceRepository>();
builder.Services.AddScoped<ICommerceRepository>(services => services.GetRequiredService<SwitchingCommerceRepository>());
builder.Services.AddScoped<PurchaseReceivingService>();
builder.Services.AddScoped<JsonPurchaseHistoryRepository>();
builder.Services.AddScoped<GoogleSheetsPurchaseHistoryRepository>();
builder.Services.AddScoped<SwitchingPurchaseHistoryRepository>();
builder.Services.AddScoped<IPurchaseHistoryRepository>(services => services.GetRequiredService<SwitchingPurchaseHistoryRepository>());
builder.Services.AddScoped<JsonSalesRepository>();
builder.Services.AddScoped<GoogleSheetsSalesRepository>();
builder.Services.AddScoped<SwitchingSalesRepository>();
builder.Services.AddScoped<ISalesRepository>(services => services.GetRequiredService<SwitchingSalesRepository>());
builder.Services.AddScoped<SalesInventoryService>();
builder.Services.AddScoped<SalesReturnService>();
builder.Services.AddScoped<SalesPaymentService>();
builder.Services.AddScoped<JsonMarketingRepository>();
builder.Services.AddScoped<GoogleSheetsMarketingRepository>();
builder.Services.AddScoped<SwitchingMarketingRepository>();
builder.Services.AddScoped<IMarketingRepository>(services => services.GetRequiredService<SwitchingMarketingRepository>());
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<SupplierAnalyticsService>();
builder.Services.AddScoped<IntermediaryAnalyticsService>();
builder.Services.AddScoped<InventoryAnalyticsService>();
builder.Services.AddScoped<ProfitAnalyticsService>();
builder.Services.AddScoped<ProductImageStorageService>();
builder.Services.AddScoped<JsonBusinessSettingsRepository>();
builder.Services.AddScoped<GoogleSheetsBusinessSettingsRepository>();
builder.Services.AddScoped<SwitchingBusinessSettingsRepository>();
builder.Services.AddScoped<IBusinessSettingsRepository>(services => services.GetRequiredService<SwitchingBusinessSettingsRepository>());
builder.Services.AddScoped<ProductStatusService>();
builder.Services.AddScoped<StockAdjustmentService>();
builder.Services.AddScoped<JsonStockMovementRepository>();
builder.Services.AddScoped<GoogleSheetsStockMovementRepository>();
builder.Services.AddScoped<SwitchingStockMovementRepository>();
builder.Services.AddScoped<IStockMovementRepository>(services => services.GetRequiredService<SwitchingStockMovementRepository>());

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapGet("/media/drive/{fileId}", async (string fileId, ProductImageStorageService storage, CancellationToken token) =>
{
    var image = await storage.DownloadDriveAsync(fileId, token);
    return Results.Stream(image.Content, image.ContentType, enableRangeProcessing: true);
});
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
