using Dona.Crm.Web.Storage;

namespace Dona.Crm.Web.Services;

public sealed class InterfaceModeState(IBusinessSettingsRepository repository)
{
    public bool IsSimple { get; private set; }
    public bool IsLoaded { get; private set; }
    public event Action? Changed;

    public async Task EnsureLoadedAsync()
    {
        if (IsLoaded) return;
        var settings = await repository.GetAsync();
        IsSimple = settings.SimpleInterfaceMode;
        IsLoaded = true;
        Changed?.Invoke();
    }

    public async Task ToggleAsync()
    {
        var settings = await repository.GetAsync();
        settings.SimpleInterfaceMode = !IsSimple;
        await repository.SaveAsync(settings);
        Set(settings.SimpleInterfaceMode);
    }

    public void Set(bool simple)
    {
        IsSimple = simple;
        IsLoaded = true;
        Changed?.Invoke();
    }
}
