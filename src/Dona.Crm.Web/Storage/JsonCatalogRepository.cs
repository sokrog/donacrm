using System.Text.Json;
using Dona.Crm.Web.Domain;
using Microsoft.Extensions.Options;

namespace Dona.Crm.Web.Storage;

public sealed class JsonCatalogRepository : ICatalogRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _filePath;

    public JsonCatalogRepository(IWebHostEnvironment environment, IOptions<StorageOptions> options) =>
        _filePath = Path.GetFullPath(options.Value.DataFile, environment.ContentRootPath);

    public async Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await ReadUnsafeAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task<Product?> GetProductAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await GetProductsAsync(cancellationToken)).FirstOrDefault(x => x.Id == id);

    public async Task UpsertProductAsync(Product product, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var products = await ReadUnsafeAsync(cancellationToken);
            var index = products.FindIndex(x => x.Id == product.Id);
            if (index >= 0) products[index] = product; else products.Add(product);
            await WriteUnsafeAsync(products, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteProductAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var products = await ReadUnsafeAsync(cancellationToken);
            products.RemoveAll(x => x.Id == id);
            await WriteUnsafeAsync(products, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task<List<Product>> ReadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath)) return SeedProducts();
        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<List<Product>>(stream, JsonOptions, cancellationToken) ?? [];
    }

    private async Task WriteUnsafeAsync(List<Product> products, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var temporaryPath = _filePath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
            await JsonSerializer.SerializeAsync(stream, products, JsonOptions, cancellationToken);
        File.Move(temporaryPath, _filePath, true);
    }

    private static List<Product> SeedProducts() =>
    [
        new() { Sku = "TS-0001", Name = "Oversize футболка Basic", Category = "Футболка", Status = ProductStatus.InStock, PurchasePriceCny = 28, DeliveryCostUzs = 18_000, SellingPriceUzs = 119_000, Variants = [new() { Color = "Черный", Size = "M", Quantity = 5 }, new() { Color = "Черный", Size = "L", Quantity = 3 }] },
        new() { Sku = "BG-0001", Name = "Сумка City Mini", Category = "Сумка", Status = ProductStatus.OnOrder, PurchasePriceCny = 42, DeliveryCostUzs = 24_000, SellingPriceUzs = 169_000 },
        new() { Sku = "HD-0001", Name = "Худи Minimal", Category = "Худи", Status = ProductStatus.LowStock, PurchasePriceCny = 75, DeliveryCostUzs = 36_000, SellingPriceUzs = 279_000, Variants = [new() { Color = "Бежевый", Size = "L", Quantity = 2 }] }
    ];
}
