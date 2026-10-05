using Dona.Crm.Web.Services;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dona.Crm.Core.Tests;

public sealed class SyncDebounceTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task RapidTriggersCollapseIntoOneRun()
    {
        var time = new FakeTimeProvider();
        var runs = 0;
        using var debouncer = new AsyncDebouncer(Delay, _ => { Interlocked.Increment(ref runs); return Task.CompletedTask; }, time);

        for (var i = 0; i < 5; i++)
        {
            debouncer.Trigger();
            time.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(0, runs);
        time.Advance(Delay);
        await debouncer.FlushAsync();
        await WaitAsync(() => Volatile.Read(ref runs) == 1);
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task TriggerDuringRunCausesExactlyOneFollowUp()
    {
        var time = new FakeTimeProvider();
        var runs = 0;
        var started = new SemaphoreSlim(0);
        var release = new TaskCompletionSource();
        using var debouncer = new AsyncDebouncer(Delay, async _ =>
        {
            var n = Interlocked.Increment(ref runs);
            started.Release();
            if (n == 1)
                await release.Task;
        }, time);

        debouncer.Trigger();
        time.Advance(Delay);
        await started.WaitAsync(TimeSpan.FromSeconds(5));
        debouncer.Trigger();
        debouncer.Trigger();
        debouncer.Trigger();
        Assert.Equal(1, runs);

        release.SetResult();
        await debouncer.FlushAsync();
        Assert.Equal(2, runs);
        time.Advance(TimeSpan.FromSeconds(10));
        await debouncer.FlushAsync();
        Assert.Equal(2, runs);
    }

    [Fact]
    public async Task FlushRunsPendingImmediatelyAndIsNoOpOtherwise()
    {
        var time = new FakeTimeProvider();
        var runs = 0;
        using var debouncer = new AsyncDebouncer(Delay, _ => { Interlocked.Increment(ref runs); return Task.CompletedTask; }, time);

        await debouncer.FlushAsync();
        Assert.Equal(0, runs);

        debouncer.Trigger();
        await debouncer.FlushAsync();
        Assert.Equal(1, runs);

        time.Advance(TimeSpan.FromSeconds(10));
        await debouncer.FlushAsync();
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task ExceptionDoesNotStopLaterRuns()
    {
        var time = new FakeTimeProvider();
        var runs = 0;
        using var debouncer = new AsyncDebouncer(Delay, _ =>
        {
            if (Interlocked.Increment(ref runs) == 1)
                throw new InvalidOperationException("boom");
            return Task.CompletedTask;
        }, time);

        debouncer.Trigger();
        await debouncer.FlushAsync();
        debouncer.Trigger();
        await debouncer.FlushAsync();

        Assert.Equal(2, runs);
    }

    [Fact]
    public async Task DisposeCancelsPendingRun()
    {
        var time = new FakeTimeProvider();
        var runs = 0;
        var debouncer = new AsyncDebouncer(Delay, _ => { Interlocked.Increment(ref runs); return Task.CompletedTask; }, time);

        debouncer.Trigger();
        debouncer.Dispose();
        time.Advance(TimeSpan.FromSeconds(10));
        await debouncer.FlushAsync();
        await Task.Delay(50);

        Assert.Equal(0, runs);
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10);
    }
}
