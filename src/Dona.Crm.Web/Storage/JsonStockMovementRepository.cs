using System.Text.Json;
using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Storage;

public sealed class JsonStockMovementRepository(IWebHostEnvironment environment) : IStockMovementRepository
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path = Path.Combine(environment.ContentRootPath, "data", "stock-movements.json");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<StockMovement>> GetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await ReadCoreAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task AddRangeAsync(IEnumerable<StockMovement> movements, CancellationToken cancellationToken = default)
    {
        var additions = movements.ToList();
        if (additions.Count == 0) return;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var all = await ReadCoreAsync(cancellationToken);
            all.AddRange(additions);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            await using (var stream = File.Create(temp)) await JsonSerializer.SerializeAsync(stream, all, Options, cancellationToken);
            File.Move(temp, _path, true);
        }
        finally { _gate.Release(); }
    }

    private async Task<List<StockMovement>> ReadCoreAsync(CancellationToken token)
    {
        if (!File.Exists(_path)) return [];
        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<List<StockMovement>>(stream, Options, token) ?? [];
    }
}
