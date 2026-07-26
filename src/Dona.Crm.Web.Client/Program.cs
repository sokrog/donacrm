using Dona.Crm.Storage.Browser;
using Dona.Crm.UI.Components;
using Dona.Crm.Web.Services;
using Dona.Crm.Web.Storage;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Routes>("#app");
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress), Timeout = TimeSpan.FromSeconds(45) });

builder.Services.AddScoped<BrowserCrmRepository>();
builder.Services.AddScoped<ICatalogRepository>(services => services.GetRequiredService<BrowserCrmRepository>());
builder.Services.AddScoped<ICommerceRepository>(services => services.GetRequiredService<BrowserCrmRepository>());
builder.Services.AddScoped<ISalesRepository>(services => services.GetRequiredService<BrowserCrmRepository>());
builder.Services.AddScoped<IMarketingRepository>(services => services.GetRequiredService<BrowserCrmRepository>());
builder.Services.AddScoped<IBusinessSettingsRepository>(services => services.GetRequiredService<BrowserCrmRepository>());
builder.Services.AddScoped<IStockMovementRepository>(services => services.GetRequiredService<BrowserCrmRepository>());
builder.Services.AddScoped<IPurchaseHistoryRepository>(services => services.GetRequiredService<BrowserCrmRepository>());

builder.Services.AddScoped<BrowserGoogleConnectionService>();
builder.Services.AddScoped<GoogleSheetsSnapshotClient>();
builder.Services.AddScoped<GoogleDriveFileClient>();
builder.Services.AddScoped<IProductImageResolver, GoogleDriveProductImageResolver>();
builder.Services.AddScoped<IGoogleConnectionService>(services => services.GetRequiredService<BrowserGoogleConnectionService>());
builder.Services.AddScoped<IGoogleAccessTokenProvider>(services => services.GetRequiredService<BrowserGoogleConnectionService>());
builder.Services.AddScoped<BrowserGoogleSyncCheckpointStore>();
builder.Services.AddScoped<IGoogleSyncCheckpointStore>(services => services.GetRequiredService<BrowserGoogleSyncCheckpointStore>());
builder.Services.AddScoped<IGoogleSyncService, BrowserGoogleSyncService>();
builder.Services.AddScoped<IProductImagePicker, BrowserProductImagePicker>();
builder.Services.AddSingleton(new AppPlatformProfile(
    "Хранилище в браузере",
    "Локальные данные · доступны офлайн на этом устройстве",
    UsesDirectGoogleAccess: true,
    RequiresOAuthClientId: true,
    OAuthClientDescription: "Web Client ID из Google Cloud Console"));

builder.Services.AddScoped<HomeDashboardService>();
builder.Services.AddScoped<ProductStatusService>();
builder.Services.AddScoped<StockAdjustmentService>();
builder.Services.AddScoped<SalesInventoryService>();
builder.Services.AddScoped<SalesPaymentService>();
builder.Services.AddScoped<SalesReturnService>();
builder.Services.AddScoped<PurchaseReceivingService>();
builder.Services.AddScoped<SupplierAnalyticsService>();
builder.Services.AddScoped<IntermediaryAnalyticsService>();
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<InventoryAnalyticsService>();
builder.Services.AddScoped<ProfitAnalyticsService>();

await builder.Build().RunAsync();
