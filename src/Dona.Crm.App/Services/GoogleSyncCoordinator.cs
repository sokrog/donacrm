using Dona.Crm.Storage.Sqlite;
using Microsoft.Maui.Networking;

namespace Dona.Crm.App.Services;

/// <summary>
/// Keeps the device-local queue in sync while the app is alive. It intentionally
/// does not promise execution after an app has been terminated by the OS.
/// </summary>
public sealed class GoogleSyncCoordinator(
    SqliteAggregateStore store,
    MauiGoogleSyncService sync) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool started;
    private bool disposed;

    public void Start()
    {
        if (started)
            return;

        started = true;
        store.BusinessDataChanged += OnBusinessDataChanged;
        Connectivity.ConnectivityChanged += OnConnectivityChanged;
        RequestFlush();
    }

    public void RequestFlush() => _ = FlushPendingAsync();

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        store.BusinessDataChanged -= OnBusinessDataChanged;
        Connectivity.ConnectivityChanged -= OnConnectivityChanged;
        gate.Dispose();
    }

    private void OnBusinessDataChanged(object? sender, EventArgs args) => _ = QueueAndFlushAsync();

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs args)
    {
        if (args.NetworkAccess == NetworkAccess.Internet)
            RequestFlush();
    }

    private async Task QueueAndFlushAsync()
    {
        try
        {
            await gate.WaitAsync();
            try
            {
                await sync.QueueLocalChangesAsync();
                if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
                    await sync.SyncQueuedAsync();
            }
            finally
            {
                gate.Release();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The sync service persists a recoverable error; an edit must never fail because sync is unavailable.
        }
    }

    private async Task FlushPendingAsync()
    {
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            return;

        try
        {
            await gate.WaitAsync();
            try
            {
                await sync.SyncQueuedAsync();
            }
            finally
            {
                gate.Release();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Network failures stay in the local queue and will be retried on the next connection/activation.
        }
    }
}
