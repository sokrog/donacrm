using Dona.Crm.Web.Domain;
using Dona.Crm.Web.Services;

namespace Dona.Crm.Web.Storage;

public sealed class SwitchingMarketingRepository(GoogleSheetsSettingsStore settings, JsonMarketingRepository local, GoogleSheetsMarketingRepository google, LoadingState loading) : IMarketingRepository
{
    private IMarketingRepository Current => settings.UseGoogleSheets ? google : local;
    public Task<MarketingData> GetDataAsync(CancellationToken token = default) => loading.RunAsync("Загружаем контент и подборки…", () => Current.GetDataAsync(token));
    public Task<IReadOnlyList<ProductCollection>> GetCollectionsAsync(CancellationToken token = default) => loading.RunAsync("Загружаем коллекции…", () => Current.GetCollectionsAsync(token));
    public Task UpsertCollectionAsync(ProductCollection value, CancellationToken token = default) => loading.RunAsync("Сохраняем коллекцию…", () => Current.UpsertCollectionAsync(value, token));
    public Task DeleteCollectionAsync(Guid id, CancellationToken token = default) => loading.RunAsync("Удаляем коллекцию…", () => Current.DeleteCollectionAsync(id, token));
    public Task<IReadOnlyList<Outfit>> GetOutfitsAsync(CancellationToken token = default) => loading.RunAsync("Загружаем образы…", () => Current.GetOutfitsAsync(token));
    public Task UpsertOutfitAsync(Outfit value, CancellationToken token = default) => loading.RunAsync("Сохраняем образ…", () => Current.UpsertOutfitAsync(value, token));
    public Task DeleteOutfitAsync(Guid id, CancellationToken token = default) => loading.RunAsync("Удаляем образ…", () => Current.DeleteOutfitAsync(id, token));
    public Task<IReadOnlyList<ContentPost>> GetContentPostsAsync(CancellationToken token = default) => loading.RunAsync("Загружаем контент-план…", () => Current.GetContentPostsAsync(token));
    public Task UpsertContentPostAsync(ContentPost value, CancellationToken token = default) => loading.RunAsync("Сохраняем публикацию…", () => Current.UpsertContentPostAsync(value, token));
    public Task DeleteContentPostAsync(Guid id, CancellationToken token = default) => loading.RunAsync("Удаляем публикацию…", () => Current.DeleteContentPostAsync(id, token));
}
