namespace Dona.Crm.Web.Services;

public sealed class LoadingState
{
    private int _operations;
    public bool IsLoading => _operations > 0;
    public string Message { get; private set; } = "Загрузка…";
    public event Action? Changed;

    public IDisposable Begin(string message)
    {
        Message = message;
        Interlocked.Increment(ref _operations);
        Changed?.Invoke();
        return new Scope(this);
    }

    public async Task<T> RunAsync<T>(string message, Func<Task<T>> operation)
    {
        using var scope = Begin(message);
        return await operation();
    }

    public async Task RunAsync(string message, Func<Task> operation)
    {
        using var scope = Begin(message);
        await operation();
    }

    private void End()
    {
        if (Interlocked.Decrement(ref _operations) < 0) Interlocked.Exchange(ref _operations, 0);
        Changed?.Invoke();
    }
    private sealed class Scope(LoadingState owner) : IDisposable { private int _disposed; public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.End(); } }
}
