using System.Diagnostics;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class CullPerfOverheadTests
{
    [WindowsFact]
    public async Task QualifiedRecordingOverhead()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("CULL_PERF_RUN") == null,
            "Use scripts/cull-perf.ps1 for isolated qualification.");
        var off = new List<double>();
        var on = new List<double>();
        long lost = 0;
        for (var i = 0; i < 9; i++)
        {
            // Interleave both orders to avoid always giving one side a warmer process.
            foreach (var enabled in i % 2 == 0 ? new[] { false, true } : new[] { true, false })
            {
                var sample = await AdjacentPreviewPerformanceTests.MeasureAsync(
                    GoldenTestPaths.Asset("display-p3-reference.jpg"), true, enabled, measureAtNotification: true);
                (enabled ? on : off).Add(sample.FirstPaintMs);
                lost += sample.LostEvents;
            }
        }
        var recorder = new CullPerfRecorder(20100);
        for (var i = 0; i < 100; i++) recorder.Record("warmup");
        var cost = new double[10000];
        // Two passes: the first absorbs one-off runtime work (tiering); the
        // steady-state allocation is the smaller of the two.
        var enabledAllocation = long.MaxValue;
        for (var pass = 0; pass < 2; pass++)
        {
            var enabledBefore = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < cost.Length; i++)
            {
                var start = Stopwatch.GetTimestamp();
                recorder.Record("Receipt", operation: -1);
                cost[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            }
            enabledAllocation = Math.Min(enabledAllocation,
                GC.GetAllocatedBytesForCurrentThread() - enabledBefore);
        }
        CullPerfRecorder? disabled = null;
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) disabled?.Record("Receipt", operation: -1);
        var allocation = GC.GetAllocatedBytesForCurrentThread() - allocated;
        var directory = Path.Combine(CullPerfFiles.Run, "recording-overhead");
        Directory.CreateDirectory(directory);
        CullPerfFiles.WriteNew(Path.Combine(directory, "pairs.json"), new { off, on, differences = on.Zip(off, (enabled, disabled) => enabled - disabled).ToArray() });
        CullPerfFiles.WriteNew(Path.Combine(directory, "fragment.json"), new CullPerfFragment(
            "recording-overhead", CullPerfFiles.Hash(CullPerfFiles.GatePath),
            Environment.GetEnvironmentVariable("CULL_PERF_MACHINE") ?? "", CullPerfFiles.Fixtures().Hashes,
            "paired-recording", true, false, recorder.LostEvents + lost, 9, 9, 0, 0, 0, [],
            new()
            {
                ["recording-delta-ms"] = on.Zip(off, (enabled, disabled) => enabled - disabled)
                    .Select((difference, index) => new CullPerfSample(index + 1, Math.Max(0, difference))).ToArray(),
                ["event-cost-us"] = cost.Select((value, index) => new CullPerfSample(index + 1, value)).ToArray(),
                ["enabled-allocated-bytes"] = [new(1, enabledAllocation)],
                ["disabled-allocated-bytes"] = [new(1, allocation)]
            }, []));
    }
}
