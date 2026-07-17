using System.Text.Json;
using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Storage;

public sealed class JsonCommerceRepository : ICommerceRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _filePath;
    public JsonCommerceRepository(IWebHostEnvironment environment) => _filePath = Path.Combine(environment.ContentRootPath, "data", "commerce.json");

    public async Task<IReadOnlyList<Supplier>> GetSuppliersAsync(CancellationToken cancellationToken = default) => (await ReadAsync(cancellationToken)).Suppliers;
    public async Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default) => (await ReadAsync(cancellationToken)).Categories;
    public async Task<IReadOnlyList<Purchase>> GetPurchasesAsync(CancellationToken cancellationToken = default) => (await ReadAsync(cancellationToken)).Purchases;
    public async Task<Purchase?> GetPurchaseAsync(Guid id, CancellationToken cancellationToken = default) => (await ReadAsync(cancellationToken)).Purchases.FirstOrDefault(x => x.Id == id);
    public Task UpsertSupplierAsync(Supplier supplier, CancellationToken cancellationToken = default) => MutateAsync(data => Upsert(data.Suppliers, supplier, x => x.Id), cancellationToken);
    public Task DeleteSupplierAsync(Guid id, CancellationToken cancellationToken = default) => MutateAsync(data => data.Suppliers.RemoveAll(x => x.Id == id), cancellationToken);
    public Task UpsertCategoryAsync(Category category, CancellationToken cancellationToken = default) => MutateAsync(data => Upsert(data.Categories, category, x => x.Id), cancellationToken);
    public Task UpsertPurchaseAsync(Purchase purchase, CancellationToken cancellationToken = default) => MutateAsync(data => Upsert(data.Purchases, purchase, x => x.Id), cancellationToken);

    private async Task<CommerceData> ReadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await ReadCoreAsync(cancellationToken); }
        finally { _gate.Release(); }
    }
    private async Task MutateAsync(Action<CommerceData> mutation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try { var data = await ReadCoreAsync(cancellationToken); mutation(data); await WriteCoreAsync(data, cancellationToken); }
        finally { _gate.Release(); }
    }
    private async Task<CommerceData> ReadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath)) return Seed();
        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<CommerceData>(stream, JsonOptions, cancellationToken) ?? Seed();
    }
    private async Task WriteCoreAsync(CommerceData data, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var temporary = _filePath + ".tmp";
        await using (var stream = File.Create(temporary)) await JsonSerializer.SerializeAsync(stream, data, JsonOptions, cancellationToken);
        File.Move(temporary, _filePath, true);
    }
    private static void Upsert<T>(List<T> values, T value, Func<T, Guid> id) { var index = values.FindIndex(x => id(x) == id(value)); if (index >= 0) values[index] = value; else values.Add(value); }
    private static CommerceData Seed() => new() { Categories = new[] { "Футболка", "Худи", "Рубашка", "Брюки", "Джинсы", "Куртка", "Сумка", "Кепка", "Ремень", "Украшения", "Другое" }.Select((name, index) => new Category { Name = name, SortOrder = index }).ToList() };
}
