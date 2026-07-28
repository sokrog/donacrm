using Microsoft.Extensions.Logging;
#if !WINDOWS
using Microsoft.Maui.DevFlow.Agent;
using Microsoft.Maui.DevFlow.Blazor;
#endif
using Dona.Crm.Storage.Sqlite;
using Dona.Crm.App.Services;
using Dona.Crm.Web.Services;
using Dona.Crm.Web.Storage;

namespace Dona.Crm.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddTransient<MainPage>();
		builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(45) });
		builder.Services.AddSingleton<ISecureValueStore, MauiSecureValueStore>();
		builder.Services.AddSingleton<IGooglePlatformAuthorization, GooglePlatformAuthorization>();
		builder.Services.AddSingleton<MauiGoogleConnectionService>();
		builder.Services.AddSingleton<IGoogleConnectionService>(services => services.GetRequiredService<MauiGoogleConnectionService>());
		builder.Services.AddSingleton<IGoogleAccessTokenProvider>(services => services.GetRequiredService<MauiGoogleConnectionService>());
		builder.Services.AddSingleton<GoogleSheetsSnapshotClient>();
		builder.Services.AddSingleton<GoogleDriveFileClient>();
		builder.Services.AddSingleton<IPersonalCloudBackupService, GoogleDrivePersonalBackupService>();
		builder.Services.AddSingleton<IProductImageResolver, GoogleDriveProductImageResolver>();
		builder.Services.AddSingleton<MauiGoogleSyncService>();
		builder.Services.AddSingleton<IGoogleSyncService>(services => services.GetRequiredService<MauiGoogleSyncService>());
		builder.Services.AddSingleton<GoogleSyncCoordinator>();
#if ANDROID
		builder.Services.AddSingleton(new AppPlatformProfile(
			"SQLite на устройстве",
			"Основное хранилище · доступно без интернета",
			UsesDirectGoogleAccess: true,
			RequiresOAuthClientId: false,
			OAuthClientDescription: "OAuth-клиент Android должен совпадать с package com.tnadevelopment.donacrm и SHA-1 подписи APK"));
#elif IOS
		builder.Services.AddSingleton(new AppPlatformProfile(
			"SQLite на устройстве",
			"Основное хранилище · доступно без интернета",
			UsesDirectGoogleAccess: true,
			RequiresOAuthClientId: false,
			OAuthClientDescription: "iOS OAuth-клиент DONA CRM встроен в приложение"));
#else
		builder.Services.AddSingleton(new AppPlatformProfile(
			"SQLite на устройстве",
			"Основное хранилище · доступно без интернета",
			UsesDirectGoogleAccess: true,
			RequiresOAuthClientId: true,
			OAuthClientDescription: "OAuth Client ID типа Desktop app из Google Cloud Console",
			SupportsOAuthClientSecret: true,
			OAuthClientSecretDescription: "Client Secret из JSON Desktop OAuth-клиента; сохраняется в защищённом хранилище Windows"));
#endif
		builder.Services.AddSingleton(new SqliteStoreOptions(
			Path.Combine(FileSystem.AppDataDirectory, SqliteStoreOptions.DatabaseFilename)));
		builder.Services.AddSingleton<SqliteAggregateStore>();
		builder.Services.AddSingleton<SqliteCatalogRepository>();
		builder.Services.AddSingleton<ICatalogRepository>(services => services.GetRequiredService<SqliteCatalogRepository>());
		builder.Services.AddSingleton<SqliteCommerceRepository>();
		builder.Services.AddSingleton<ICommerceRepository>(services => services.GetRequiredService<SqliteCommerceRepository>());
		builder.Services.AddSingleton<SqliteSalesRepository>();
		builder.Services.AddSingleton<ISalesRepository>(services => services.GetRequiredService<SqliteSalesRepository>());
		builder.Services.AddSingleton<SqliteMarketingRepository>();
		builder.Services.AddSingleton<IMarketingRepository>(services => services.GetRequiredService<SqliteMarketingRepository>());
		builder.Services.AddSingleton<SqliteBusinessSettingsRepository>();
		builder.Services.AddSingleton<IBusinessSettingsRepository>(services => services.GetRequiredService<SqliteBusinessSettingsRepository>());
		builder.Services.AddSingleton<SqliteStockMovementRepository>();
		builder.Services.AddSingleton<IStockMovementRepository>(services => services.GetRequiredService<SqliteStockMovementRepository>());
		builder.Services.AddSingleton<SqlitePurchaseHistoryRepository>();
		builder.Services.AddSingleton<IPurchaseHistoryRepository>(services => services.GetRequiredService<SqlitePurchaseHistoryRepository>());
		builder.Services.AddSingleton<SqliteSyncStore>();
		builder.Services.AddSingleton<IBackupSnapshotStore>(services => services.GetRequiredService<SqliteSyncStore>());
		builder.Services.AddSingleton<BackupRestoreService>();
		builder.Services.AddSingleton<IBackupArchiveFileService, MauiBackupArchiveFileService>();
		builder.Services.AddSingleton<SqliteSyncOperationStore>();
		builder.Services.AddSingleton<SqliteSyncCheckpointStore>();
		builder.Services.AddSingleton<IGoogleSyncCheckpointStore>(services => services.GetRequiredService<SqliteSyncCheckpointStore>());
		builder.Services.AddSingleton<HomeDashboardService>();
		builder.Services.AddSingleton<ProductStatusService>();
		builder.Services.AddSingleton<IProductImagePicker, MauiProductImagePicker>();
		builder.Services.AddSingleton<StockAdjustmentService>();
		builder.Services.AddSingleton<SalesInventoryService>();
		builder.Services.AddSingleton<SalesPaymentService>();
		builder.Services.AddSingleton<SalesReturnService>();
		builder.Services.AddSingleton<PurchaseReceivingService>();
		builder.Services.AddSingleton<SupplierAnalyticsService>();
		builder.Services.AddSingleton<IntermediaryAnalyticsService>();
		builder.Services.AddSingleton<AnalyticsService>();
		builder.Services.AddSingleton<InventoryAnalyticsService>();
		builder.Services.AddSingleton<ProfitAnalyticsService>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#if !WINDOWS
		builder.AddMauiDevFlowAgent();
		builder.AddMauiBlazorDevFlowTools();
#endif
#endif

		return builder.Build();
	}
}
