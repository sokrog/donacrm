using Microsoft.Extensions.Logging;
using Microsoft.Maui.DevFlow.Agent;
using Microsoft.Maui.DevFlow.Blazor;
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
		builder.Services.AddSingleton(new SqliteStoreOptions(
			Path.Combine(FileSystem.AppDataDirectory, SqliteStoreOptions.DatabaseFilename)));
		builder.Services.AddSingleton<SqliteAggregateStore>();
		builder.Services.AddSingleton<ICatalogRepository, SqliteCatalogRepository>();
		builder.Services.AddSingleton<ICommerceRepository, SqliteCommerceRepository>();
		builder.Services.AddSingleton<ISalesRepository, SqliteSalesRepository>();
		builder.Services.AddSingleton<IMarketingRepository, SqliteMarketingRepository>();
		builder.Services.AddSingleton<IBusinessSettingsRepository, SqliteBusinessSettingsRepository>();
		builder.Services.AddSingleton<IStockMovementRepository, SqliteStockMovementRepository>();
		builder.Services.AddSingleton<IPurchaseHistoryRepository, SqlitePurchaseHistoryRepository>();
		builder.Services.AddSingleton<HomeDashboardService>();
		builder.Services.AddSingleton<ProductStatusService>();
		builder.Services.AddSingleton<IProductImagePicker, MauiProductImagePicker>();
		builder.Services.AddSingleton<StockAdjustmentService>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
		builder.AddMauiDevFlowAgent();
		builder.AddMauiBlazorDevFlowTools();
#endif

		return builder.Build();
	}
}
