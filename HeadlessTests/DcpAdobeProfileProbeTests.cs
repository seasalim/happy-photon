using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DcpAdobeProfileProbeTests
{
    [AvaloniaFact]
    public async Task StalledProbeHonorsBudgetWithoutBlockingDispatcher()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var watch = Stopwatch.StartNew();
        var probe = DcpAdobeProfileIndex.ProbeAsync(["stalled"], budget: TimeSpan.FromMilliseconds(200),
            enumerateDirectory: _ =>
            {
                entered.TrySetResult();
                release.Task.WaitAsync(TestWaits.Condition).GetAwaiter().GetResult();
                exited.TrySetResult();

                return [];
            });

        try
        {
            await entered.Task.WaitAsync(TestWaits.Condition);
            // test-wait-policy: allow - G3 explicitly posts a dispatcher job 50 ms into a stalled probe.
            await Task.Delay(50);
            var dispatched = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
            var posted = Stopwatch.StartNew();
            Dispatcher.UIThread.Post(() => dispatched.TrySetResult(posted.Elapsed.TotalMilliseconds));
            var latency = await dispatched.Task.WaitAsync(TestWaits.Condition);
            Assert.Equal(DcpAdobeProfilePresence.Unknown, await probe.WaitAsync(TestWaits.Condition));
            Assert.InRange(watch.Elapsed.TotalMilliseconds, 200, 400);
            Assert.True(latency <= 50, $"Dispatcher job took {latency:F3} ms.");
        }
        finally
        {
            release.TrySetResult();
            await exited.Task.WaitAsync(TestWaits.Condition);
        }
    }
}
