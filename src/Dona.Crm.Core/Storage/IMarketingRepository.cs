using Dona.Crm.Web.Domain;

namespace Dona.Crm.Web.Storage;

public interface IMarketingRepository
{
    Task<MarketingData> GetDataAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductCollection>> GetCollectionsAsync(CancellationToken cancellationToken = default);
    Task UpsertCollectionAsync(ProductCollection collection, CancellationToken cancellationToken = default);
    Task DeleteCollectionAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Outfit>> GetOutfitsAsync(CancellationToken cancellationToken = default);
    Task UpsertOutfitAsync(Outfit outfit, CancellationToken cancellationToken = default);
    Task DeleteOutfitAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ContentPost>> GetContentPostsAsync(CancellationToken cancellationToken = default);
    Task UpsertContentPostAsync(ContentPost post, CancellationToken cancellationToken = default);
    Task DeleteContentPostAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class MarketingData
{
    public List<ProductCollection> Collections { get; set; } = [];
    public List<Outfit> Outfits { get; set; } = [];
    public List<ContentPost> ContentPosts { get; set; } = [];
}
