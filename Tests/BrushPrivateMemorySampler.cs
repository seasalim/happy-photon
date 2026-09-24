using System.Diagnostics;

namespace HappyPhoton.Tests;

// Report-only export private peak sampler. Private bytes reflect process/GC commitment.
internal sealed class BrushPrivateMemorySampler : IDisposable
{
    private readonly Process process = Process.GetCurrentProcess();
    private readonly object sync = new();
    private readonly ManualResetEventSlim stop = new();
    private readonly Task sampler;

    internal long Baseline { get; }
    internal long Peak { get; private set; }
    internal BrushPrivateMemorySampler()
    {
        process.Refresh(); Peak = Baseline = process.PrivateMemorySize64;
        // A dedicated sampler must not wait behind raster tasks in the thread pool.
        sampler = Task.Factory.StartNew(() =>
        {
            while (!stop.Wait(1)) Observe();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private void Observe()
    {
        lock (sync)
        {
            process.Refresh(); var value = process.PrivateMemorySize64;
            Peak = Math.Max(Peak, value);
        }
    }

    internal void Finish()
    {
        stop.Set(); sampler.GetAwaiter().GetResult(); Observe();
    }
    public void Dispose() { Finish(); stop.Dispose(); process.Dispose(); }
}
