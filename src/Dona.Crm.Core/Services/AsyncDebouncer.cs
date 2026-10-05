namespace Dona.Crm.Web.Services;

/// <summary>
/// Collapses bursts of triggers into a single asynchronous run. The action never overlaps itself:
/// a trigger that arrives while it runs schedules exactly one follow-up run right after it finishes.
/// Exceptions from the action are swallowed so later runs keep working.
/// </summary>
public sealed class AsyncDebouncer : IDisposable, IAsyncDisposable
{
    private readonly TimeSpan delay;
    private readonly Func<CancellationToken, Task> action;
    private readonly TimeProvider timeProvider;
    private readonly CancellationTokenSource lifetime = new();
    private readonly object sync = new();
    private CancellationTokenSource? timer;
    private Task runTask = Task.CompletedTask;
    private bool running;
    private bool pending;
    private bool disposed;

    public AsyncDebouncer(TimeSpan delay, Func<CancellationToken, Task> action, TimeProvider? timeProvider = null)
    {
        this.delay = delay;
        this.action = action;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>(Re)starts the delay; the action runs once the triggers stop for the configured delay.</summary>
    public void Trigger()
    {
        lock (sync)
        {
            if (disposed)
                return;

            pending = true;
            if (running)
                return; // the running drain loop picks up exactly one follow-up run

            CancelTimerLocked();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timer = cts;
            _ = WaitAndRunAsync(cts);
        }
    }

    /// <summary>Runs a pending action immediately (or waits for the running one) and awaits completion.</summary>
    public Task FlushAsync()
    {
        lock (sync)
        {
            if (disposed)
                return Task.CompletedTask;

            CancelTimerLocked();
            if (!running)
            {
                if (!pending)
                    return Task.CompletedTask;
                StartRunLocked();
            }

            return runTask;
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
                return;

            disposed = true;
            pending = false;
            CancelTimerLocked();
        }

        lifetime.Cancel();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task WaitAndRunAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(delay, timeProvider, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (sync)
        {
            // A newer trigger, flush or dispose replaced this timer while the delay was completing.
            if (disposed || !ReferenceEquals(timer, cts))
                return;

            timer = null;
            cts.Dispose();
            if (!running && pending)
                StartRunLocked();
        }
    }

    private void StartRunLocked()
    {
        running = true;
        runTask = Task.Run(DrainAsync);
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            lock (sync)
            {
                if (disposed || !pending)
                {
                    running = false;
                    return;
                }

                pending = false;
            }

            try
            {
                await action(lifetime.Token).ConfigureAwait(false);
            }
            catch
            {
                // Recoverable errors are persisted by the action's owner; keep later runs alive.
            }
        }
    }

    private void CancelTimerLocked()
    {
        var current = timer;
        timer = null;
        if (current is null)
            return;

        current.Cancel();
        current.Dispose();
    }
}
