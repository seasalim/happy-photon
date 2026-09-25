using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

// Report only: run alone in a fresh Release process, before and after a change.
[Collection(AvaloniaTestCollection.Name)]
public sealed class RenderSharpeningRetentionPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public void LuminanceSharpening_ReportsRetainedManagedBytes_WhenEnabled()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to run sharpening retention diagnostics.");
        PerfEnvironment.AssertFullCpu();
        using var previewA = CreateImage(3200, 2133);
        using var fullA = CreateImage(6000, 4000);
        using var previewB = CreateImage(3200, 2133);
        using var fullB = CreateImage(6000, 4000);
        var images = new[] { previewA, fullA, previewB, fullB };
        using var release = new ManualResetEventSlim();
        using var pairStart = new ManualResetEventSlim();
        var workers = Enumerable.Range(0, 5)
            .Select(index => new Worker(images, index == 4 ? 2 : 0,
                index >= 3 ? pairStart : null, release)).ToArray();
        try
        {
            foreach (var worker in workers) worker.Start();
            foreach (var worker in workers) worker.WaitReady();
            // Images (native Q16 storage), workers and wait handles already exist.
            // Do not warm sharpening: that would hide a retained scratch slot.
            var baseline = CollectManagedBytes();
            var stopwatch = Stopwatch.StartNew();
            foreach (var worker in workers.Take(3))
            {
                worker.Run();
                worker.WaitDone();
            }
            workers[3].Run();
            workers[4].Run();
            pairStart.Set();
            workers[3].WaitDone();
            workers[4].WaitDone();
            foreach (var image in images) image.Dispose();
            // Keep callers alive through collection, as idle pool callers would be:
            // exiting dedicated threads would erase the TLS retention under test.
            var after = CollectManagedBytes();
            stopwatch.Stop();
            Assert.All(workers, worker => Assert.True(worker.IsAlive,
                "Sharpen callers must remain alive through retained-memory collection."));
            var report = new
            {
                ProcessId = Environment.ProcessId,
                ProcessorCount = Environment.ProcessorCount,
                Workload = "Output Print: 3200x2133 then 6000x4000; 3 sequential threads, then 2 concurrent threads",
                ThreadIds = workers.Select(worker => worker.ThreadId).ToArray(),
                BaselineManagedBytes = baseline,
                AfterManagedBytes = after,
                RetainedManagedBytes = after - baseline,
                PreviewScratchBytes = ScratchBytes(3200, 2133),
                LargestScratchBytes = ScratchBytes(6000, 4000),
                ElapsedMs = stopwatch.Elapsed.TotalMilliseconds
            };
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
            output.WriteLine(json);
            if (Environment.GetEnvironmentVariable("HAPPY_PHOTON_STAGE_REPORT_DIR") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory,
                    nameof(LuminanceSharpening_ReportsRetainedManagedBytes_WhenEnabled) + ".json"), json);
            }
            Assert.Equal(5, report.ThreadIds.Distinct().Count());
        }
        finally
        {
            release.Set();
            pairStart.Set();
            foreach (var worker in workers) worker.Dispose();
        }
    }

    private static MagickImage CreateImage(uint width, uint height) =>
        new("gradient:#182a48-#edce91", new MagickReadSettings { Width = width, Height = height });

    private static long ScratchBytes(int width, int height)
    {
        var parameters = RenderSharpening.ResolveOutputParameters(
            OutputSharpeningMode.Print, Math.Max(width, height), wasResized: true)!.Value;
        var radius = Math.Max(1, (int)Math.Ceiling(parameters.Sigma * 3));
        var rows = Math.Min(height, Math.Max(1, RenderKernelSupport.DefaultBandPixelLimit / width));
        // Requested float payload, not ArrayPool's rounded bucket capacity.
        return (long)(rows + 2 * radius) * width * sizeof(float);
    }

    private static long CollectManagedBytes()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    private sealed class Worker : IDisposable
    {
        private readonly ManualResetEventSlim ready = new();
        private readonly ManualResetEventSlim run = new();
        private readonly ManualResetEventSlim done = new();
        private readonly Thread thread;
        private Exception? error;
        public int ThreadId => thread.ManagedThreadId;
        public bool IsAlive => thread.IsAlive;

        public Worker(MagickImage[] images, int offset,
            ManualResetEventSlim? pairStart, ManualResetEventSlim release)
        {
            thread = new Thread(() =>
            {
                ready.Set();
                try
                {
                    if (!run.Wait(TestWaits.Condition)) throw new TimeoutException("Sharpen worker not started.");
                    if (pairStart is not null && !pairStart.Wait(TestWaits.Condition))
                        throw new TimeoutException("Concurrent pair not released.");
                    Sharpen(images, offset);
                }
                catch (Exception exception) { error = exception; }
                finally { done.Set(); }
                release.Wait(TestWaits.Condition);
            }) { IsBackground = true };
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Sharpen(MagickImage[] images, int offset)
        {
            RenderSharpening.ApplyOutput(images[offset], OutputSharpeningMode.Print, wasResized: true);
            RenderSharpening.ApplyOutput(images[offset + 1], OutputSharpeningMode.Print, wasResized: true);
        }

        public void Start() => thread.Start();
        public void Run() => run.Set();
        public void WaitReady() => Assert.True(ready.Wait(TestWaits.Condition));
        public void WaitDone()
        {
            Assert.True(done.Wait(TestWaits.Condition), "Sharpen worker did not finish.");
            Assert.Null(error);
        }

        public void Dispose()
        {
            run.Set();
            if (thread.IsAlive && !thread.Join(TestWaits.Condition))
                throw new TimeoutException("Sharpen worker did not exit.");
            ready.Dispose();
            run.Dispose();
            done.Dispose();
        }
    }
}
