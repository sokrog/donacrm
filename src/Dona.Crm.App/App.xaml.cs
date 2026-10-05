using Dona.Crm.App.Services;

namespace Dona.Crm.App;

public partial class App : Application
{
    private readonly IServiceProvider services;
    private readonly GoogleSyncCoordinator syncCoordinator;

    public App(IServiceProvider services, GoogleSyncCoordinator syncCoordinator)
    {
        this.services = services;
        this.syncCoordinator = syncCoordinator;
        InitializeComponent();
        syncCoordinator.Start();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(services.GetRequiredService<MainPage>()) { Title = "DONA CRM" };
        window.Activated += (_, _) => syncCoordinator.RequestFlush();
        // Persist a pending debounced change before the OS may suspend the app.
        window.Deactivated += (_, _) => _ = syncCoordinator.FlushChangesAsync();
        window.Stopped += (_, _) => _ = syncCoordinator.FlushChangesAsync();
        return window;
    }
}
