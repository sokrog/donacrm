using Microsoft.Extensions.Logging;
using Microsoft.Maui.DevFlow.Agent;
using Microsoft.Maui.DevFlow.Blazor;

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

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
		builder.AddMauiDevFlowAgent();
		builder.AddMauiBlazorDevFlowTools();
#endif

		return builder.Build();
	}
}
