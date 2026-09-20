using System.Diagnostics;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Tests;

internal sealed class CullPerfProcessSampler : IDisposable
{
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _sampling;
    private readonly TimeSpan _cpuStart;
    private long _peak;
    private int _cachePeak;

    internal CullPerfProcessSampler(MainWindowViewModel vm)
    {
        _cpuStart = _process.TotalProcessorTime;
        _sampling = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                _process.Refresh();
                _peak = Math.Max(_peak, _process.PrivateMemorySize64);
                _cachePeak = Math.Max(_cachePeak, vm.ImageService.Previews.PendingCacheWrites);
                // A periodic observation, never a correctness wait.
                await Task.Delay(10, _stop.Token);
            }
        });
    }

    internal async Task<Dictionary<string, double>> FinishAsync(MainWindowViewModel vm)
    {
        await _stop.CancelAsync();
        try { await _sampling; } catch (OperationCanceledException) { }
        _process.Refresh();
        var result = new Dictionary<string, double>
        {
            ["cpu-ms"] = (_process.TotalProcessorTime - _cpuStart).TotalMilliseconds,
            ["peak-private-bytes"] = Math.Max(_peak, _process.PrivateMemorySize64),
            ["ordinary-idle-private-bytes"] = _process.PrivateMemorySize64,
            ["thumbnail-bytes"] = vm.CombinedThumbnailBytes,
            ["cache-backlog-maximum"] = _cachePeak,
            ["peak-thumbnail-bytes"] = vm.PeakThumbnailBytes,
            ["cache-backlog-at-idle"] = vm.ImageService.Previews.PendingCacheWrites,
            ["retained-base-pairs"] = vm.ImageService.Previews.RetainedBasePairCount
        };
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
        _process.Refresh();
        result["forced-gc-private-bytes"] = _process.PrivateMemorySize64;
        return result;
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _sampling.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        _stop.Dispose();
        _process.Dispose();
    }
}
