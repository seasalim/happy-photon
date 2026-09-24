using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LoadingMessageGraceConcurrencyTests
{
    [Fact]
    public async Task ConcurrentFalseTrueTransitionsKeepCancellationOwnership()
    {
        var clock = new TestTimeProvider();
        using var grace = new LoadingMessageGrace(clock, _ => { });
        using var start = new Barrier(3);
        Task Run(Action action) => Task.Run(() =>
        {
            Assert.True(start.SignalAndWait(TestWaits.Condition));
            for (var i = 0; i < 10000; i++) action();
        });
        await Task.WhenAll(
            Run(() => { grace.Update(false); grace.Update(true); }),
            Run(() => { grace.Update(false); grace.Update(true); }),
            Run(() => clock.Advance(TimeSpan.FromMilliseconds(300))))
            .WaitAsync(TestWaits.Condition);
        grace.Dispose();
        Assert.Equal(0, clock.TimerCount);
    }

    [Fact]
    public async Task ConcurrentTransitionsAndExpiryCannotResurrectDisposedOwner()
    {
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var clock = new TestTimeProvider();
            var visible = false;
            var disposed = false;
            var staleShows = 0;
            using var start = new Barrier(3);
            using var grace = new LoadingMessageGrace(clock, value =>
            {
                if (value && Volatile.Read(ref disposed)) Interlocked.Increment(ref staleShows);
                Volatile.Write(ref visible, value);
            });
            grace.Update(true);
            Task Run(Action action) => Task.Run(() =>
            {
                Assert.True(start.SignalAndWait(TestWaits.Condition));
                action();
            });
            await Task.WhenAll(
                Run(() => { grace.Update(false); grace.Update(true); }),
                Run(() => clock.Advance(TimeSpan.FromMilliseconds(300))),
                Run(() => { grace.Dispose(); Volatile.Write(ref disposed, true); }))
                .WaitAsync(TestWaits.Condition);
            grace.Update(true);
            clock.Advance(TimeSpan.FromMilliseconds(300));
            Assert.False(visible);
            Assert.Equal(0, staleShows);
            Assert.Equal(0, clock.TimerCount);
        }
    }

    [Fact]
    public async Task HeldExpiryCannotShowAfterConcurrentReplacementAndDisposal()
    {
        var clock = new TestTimeProvider();
        using var context = new HeldContext();
        var previous = SynchronizationContext.Current;
        var shows = 0;
        using var grace = new LoadingMessageGrace(clock, value =>
        {
            if (value) Interlocked.Increment(ref shows);
        });
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            grace.Update(true);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        // Hold the firing timer inside Post while another worker replaces and disposes
        // its interval. Then release the old continuation without pumping a dispatcher.
        var firing = Task.Run(() => clock.Advance(TimeSpan.FromMilliseconds(300)));
        try
        {
            var expiry = await context.Pending.Task.WaitAsync(TestWaits.Condition);
            await Task.Run(() => { grace.Update(false); grace.Update(true); grace.Dispose(); })
                .WaitAsync(TestWaits.Condition);
            context.Release.Set();
            await firing.WaitAsync(TestWaits.Condition);
            await Task.Run(() => expiry.Callback(expiry.State));
        }
        finally
        {
            context.Release.Set();
            await firing.WaitAsync(TestWaits.Condition);
        }
        clock.Advance(TimeSpan.FromMilliseconds(300));
        Assert.Equal(0, shows);
        Assert.Equal(0, clock.TimerCount);
    }

    private sealed class HeldContext : SynchronizationContext, IDisposable
    {
        internal TaskCompletionSource<(SendOrPostCallback Callback, object? State)> Pending { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Release { get; } = new();
        public override void Post(SendOrPostCallback callback, object? state)
        {
            Pending.TrySetResult((callback, state));
            Assert.True(Release.Wait(TestWaits.Condition));
        }
        public void Dispose() => Release.Dispose();
    }
}
