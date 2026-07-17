using System.Text.Json;
using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Storage;

public sealed class JsonSalesRepository : ISalesRepository
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public JsonSalesRepository(IWebHostEnvironment environment) => _path = Path.Combine(environment.ContentRootPath, "data", "sales.json");
    public async Task<IReadOnlyList<Customer>> GetCustomersAsync(CancellationToken cancellationToken = default) => (await ReadAsync(cancellationToken)).Customers;
    public async Task<IReadOnlyList<Sale>> GetSalesAsync(CancellationToken cancellationToken = default) => (await ReadAsync(cancellationToken)).Sales;
    public async Task<Sale?> GetSaleAsync(Guid id, CancellationToken cancellationToken = default) => (await ReadAsync(cancellationToken)).Sales.FirstOrDefault(x => x.Id == id);
    public Task UpsertCustomerAsync(Customer customer, CancellationToken cancellationToken = default) => MutateAsync(x => Upsert(x.Customers, customer, y => y.Id), cancellationToken);
    public Task DeleteCustomerAsync(Guid id, CancellationToken cancellationToken = default) => MutateAsync(x => x.Customers.RemoveAll(y => y.Id == id), cancellationToken);
    public Task UpsertSaleAsync(Sale sale, CancellationToken cancellationToken = default) => MutateAsync(x => Upsert(x.Sales, sale, y => y.Id), cancellationToken);
    private async Task<SalesData> ReadAsync(CancellationToken token) { await _gate.WaitAsync(token); try { return await ReadCoreAsync(token); } finally { _gate.Release(); } }
    private async Task MutateAsync(Action<SalesData> mutation, CancellationToken token) { await _gate.WaitAsync(token); try { var data = await ReadCoreAsync(token); mutation(data); await WriteCoreAsync(data, token); } finally { _gate.Release(); } }
    private async Task<SalesData> ReadCoreAsync(CancellationToken token) { if (!File.Exists(_path)) return new SalesData(); await using var stream = File.OpenRead(_path); return await JsonSerializer.DeserializeAsync<SalesData>(stream, Options, token) ?? new SalesData(); }
    private async Task WriteCoreAsync(SalesData data, CancellationToken token) { Directory.CreateDirectory(Path.GetDirectoryName(_path)!); var temp = _path + ".tmp"; await using (var stream = File.Create(temp)) await JsonSerializer.SerializeAsync(stream, data, Options, token); File.Move(temp, _path, true); }
    private static void Upsert<T>(List<T> list, T item, Func<T, Guid> id) { var index = list.FindIndex(x => id(x) == id(item)); if (index >= 0) list[index] = item; else list.Add(item); }
}
