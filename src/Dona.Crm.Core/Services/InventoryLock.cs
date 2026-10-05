namespace Dona.Crm.Web.Services;

/// <summary>
/// Единая блокировка для всех операций, которые читают и меняют остатки товаров.
/// Не реентерабельна: сервисы склада не должны вызывать друг друга, пока держат её.
/// </summary>
public static class InventoryLock
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        return new Releaser();
    }

    private sealed class Releaser : IDisposable
    {
        private int released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0) Gate.Release();
        }
    }
}
