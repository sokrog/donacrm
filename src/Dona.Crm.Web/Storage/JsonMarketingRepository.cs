using System.Text.Json;
using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Storage;

public sealed class JsonMarketingRepository : IMarketingRepository
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public JsonMarketingRepository(IWebHostEnvironment environment) => _path = Path.Combine(environment.ContentRootPath, "data", "marketing.json");
    public Task<MarketingData> GetDataAsync(CancellationToken token = default) => ReadAsync(token);
    public async Task<IReadOnlyList<ProductCollection>> GetCollectionsAsync(CancellationToken token = default) => (await ReadAsync(token)).Collections;
    public async Task<IReadOnlyList<Outfit>> GetOutfitsAsync(CancellationToken token = default) => (await ReadAsync(token)).Outfits;
    public async Task<IReadOnlyList<ContentPost>> GetContentPostsAsync(CancellationToken token = default) => (await ReadAsync(token)).ContentPosts;
    public Task UpsertCollectionAsync(ProductCollection value, CancellationToken token = default) => MutateAsync(x => Upsert(x.Collections, value, y => y.Id), token);
    public Task DeleteCollectionAsync(Guid id, CancellationToken token = default) => MutateAsync(x => { x.Collections.RemoveAll(y => y.Id == id); foreach (var post in x.ContentPosts.Where(y => y.CollectionId == id)) { post.CollectionId = null; post.CollectionName = null; } }, token);
    public Task UpsertOutfitAsync(Outfit value, CancellationToken token = default) => MutateAsync(x => Upsert(x.Outfits, value, y => y.Id), token);
    public Task DeleteOutfitAsync(Guid id, CancellationToken token = default) => MutateAsync(x => { x.Outfits.RemoveAll(y => y.Id == id); foreach (var post in x.ContentPosts.Where(y => y.OutfitId == id)) { post.OutfitId = null; post.OutfitName = null; } }, token);
    public Task UpsertContentPostAsync(ContentPost value, CancellationToken token = default) => MutateAsync(x => Upsert(x.ContentPosts, value, y => y.Id), token);
    public Task DeleteContentPostAsync(Guid id, CancellationToken token = default) => MutateAsync(x => x.ContentPosts.RemoveAll(y => y.Id == id), token);
    private async Task<MarketingData> ReadAsync(CancellationToken token) { await _gate.WaitAsync(token); try { return await ReadCoreAsync(token); } finally { _gate.Release(); } }
    private async Task MutateAsync(Action<MarketingData> action, CancellationToken token) { await _gate.WaitAsync(token); try { var data = await ReadCoreAsync(token); action(data); await WriteCoreAsync(data, token); } finally { _gate.Release(); } }
    private async Task<MarketingData> ReadCoreAsync(CancellationToken token) { if (!File.Exists(_path)) return new MarketingData(); await using var stream = File.OpenRead(_path); return await JsonSerializer.DeserializeAsync<MarketingData>(stream, Options, token) ?? new MarketingData(); }
    private async Task WriteCoreAsync(MarketingData data, CancellationToken token) { Directory.CreateDirectory(Path.GetDirectoryName(_path)!); var temp = _path + ".tmp"; await using (var stream = File.Create(temp)) await JsonSerializer.SerializeAsync(stream, data, Options, token); File.Move(temp, _path, true); }
    private static void Upsert<T>(List<T> list, T value, Func<T, Guid> id) { var index = list.FindIndex(x => id(x) == id(value)); if (index >= 0) list[index] = value; else list.Add(value); }
}
