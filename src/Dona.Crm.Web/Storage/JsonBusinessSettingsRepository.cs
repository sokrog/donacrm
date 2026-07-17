using System.Text.Json;
using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Storage;

public sealed class JsonBusinessSettingsRepository(IWebHostEnvironment environment) : IBusinessSettingsRepository
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path = Path.Combine(environment.ContentRootPath, "data", "business-settings.json");
    public async Task<BusinessSettings> GetAsync(CancellationToken token = default) { if (!File.Exists(_path)) return new BusinessSettings(); await using var stream = File.OpenRead(_path); return await JsonSerializer.DeserializeAsync<BusinessSettings>(stream, Options, token) ?? new BusinessSettings(); }
    public async Task SaveAsync(BusinessSettings settings, CancellationToken token = default) { Directory.CreateDirectory(Path.GetDirectoryName(_path)!); var temp = _path + ".tmp"; await using (var stream = File.Create(temp)) await JsonSerializer.SerializeAsync(stream, settings, Options, token); File.Move(temp, _path, true); }
}
