using Microsoft.JSInterop;

namespace Dona.Crm.Storage.Browser;

/// <summary>Хранилище строк в IndexedDB браузера (window.donaStore) с прозрачной миграцией из localStorage.</summary>
internal sealed class BrowserKeyValueStore(IJSRuntime javascript)
{
    internal const string QuotaMessage = "Хранилище браузера заполнено. Освободите место или подключите Google Drive для фотографий.";

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        try { return await javascript.InvokeAsync<string?>("donaStore.get", cancellationToken, key); }
        catch (JSException exception) { throw Translate(exception); }
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        try { await javascript.InvokeVoidAsync("donaStore.set", cancellationToken, key, value); }
        catch (JSException exception) { throw Translate(exception); }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try { await javascript.InvokeVoidAsync("donaStore.remove", cancellationToken, key); }
        catch (JSException exception) { throw Translate(exception); }
    }

    public async Task<IReadOnlyList<string>> KeysAsync(string prefix, CancellationToken cancellationToken = default)
    {
        try { return await javascript.InvokeAsync<string[]?>("donaStore.keys", cancellationToken, prefix) ?? []; }
        catch (JSException exception) { throw Translate(exception); }
    }

    private static Exception Translate(JSException exception) =>
        exception.Message.Contains("QuotaExceeded", StringComparison.OrdinalIgnoreCase) || exception.Message.Contains("заполнено", StringComparison.OrdinalIgnoreCase)
            ? new InvalidOperationException(QuotaMessage, exception)
            : exception;
}
