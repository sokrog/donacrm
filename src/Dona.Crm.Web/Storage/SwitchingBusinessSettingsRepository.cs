using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;

namespace Dona.Crm.Web.Storage;

public sealed class SwitchingBusinessSettingsRepository(GoogleSheetsSettingsStore connection, JsonBusinessSettingsRepository local, GoogleSheetsBusinessSettingsRepository google, LoadingState loading) : IBusinessSettingsRepository
{
    private BusinessSettings? _cache;
    private int _version = -1;
    private IBusinessSettingsRepository Current => connection.UseGoogleSheets ? google : local;
    public async Task<BusinessSettings> GetAsync(CancellationToken token = default) { if (_cache is not null && _version == connection.Version) return _cache; _cache = await loading.RunAsync("Загружаем бизнес-настройки…", () => Current.GetAsync(token)); _version = connection.Version; return _cache; }
    public async Task SaveAsync(BusinessSettings settings, CancellationToken token = default) { await loading.RunAsync("Сохраняем бизнес-настройки…", () => Current.SaveAsync(settings, token)); _cache = settings; _version = connection.Version; }
}
