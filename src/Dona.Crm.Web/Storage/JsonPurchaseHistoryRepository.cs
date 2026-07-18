using System.Text.Json;
using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Storage;

public sealed class JsonPurchaseHistoryRepository(IWebHostEnvironment environment) : IPurchaseHistoryRepository
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path = Path.Combine(environment.ContentRootPath, "data", "purchase-history.json");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<PurchaseHistoryData> GetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await ReadCoreAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task AddAsync(IEnumerable<ProductCostHistoryEntry> productCosts, ExchangeRateHistoryEntry? exchangeRate, CancellationToken cancellationToken = default)
    {
        var additions = productCosts.ToList();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var data = await ReadCoreAsync(cancellationToken);
            var costIds = data.ProductCosts.Select(x => x.Id).ToHashSet();
            data.ProductCosts.AddRange(additions.Where(x => costIds.Add(x.Id)));
            if (exchangeRate is not null && data.ExchangeRates.All(x => x.Id != exchangeRate.Id)) data.ExchangeRates.Add(exchangeRate);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            await using (var stream = File.Create(temp)) await JsonSerializer.SerializeAsync(stream, data, Options, cancellationToken);
            File.Move(temp, _path, true);
        }
        finally { _gate.Release(); }
    }

    private async Task<PurchaseHistoryData> ReadCoreAsync(CancellationToken token)
    {
        if (!File.Exists(_path)) return new PurchaseHistoryData();
        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<PurchaseHistoryData>(stream, Options, token) ?? new PurchaseHistoryData();
    }
}
