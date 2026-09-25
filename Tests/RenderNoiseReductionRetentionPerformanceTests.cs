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
public sealed class RenderNoiseReductionRetentionPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public void NoiseReduction_ReportsRetainedManagedBytes_WhenEnabled()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to run noise reduction retention diagnostics.");
        PerfEnvironment.AssertFullCpu();
        using var previewA = CreateImage(3200, 2133);
        using var fullA = CreateImage(6000, 4000);
        using var previewB = CreateImage(3200, 2133);
        using var fullB = CreateImage(6000, 4000);
        var images = new[] { previewA, fullA, previewB, fullB };
        Assert.All(images, image => Assert.Equal(3U, image.ChannelCount));
        var previewScratch = ScratchGeometry(3200, 2133);
        var fullScratch = ScratchGeometry(6000, 4000);
        var largest = LargestRoles(previewScratch, fullScratch);
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
            // Do not warm NR: that would hide a retained scratch slot.
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
                "NR callers must remain alive through retained-memory collection."));
            var report = new
            {
                ProcessId = Environment.ProcessId,
                ProcessorCount = Environment.ProcessorCount,
                Workload = "LuminanceNr=50, ChromaNr=50, native RAW 6000x4000: 3200x2133 then 6000x4000; 3 sequential threads, then 2 concurrent threads",
                ThreadIds = workers.Select(worker => worker.ThreadId).ToArray(),
                BaselineManagedBytes = baseline,
                AfterManagedBytes = after,
                RetainedManagedBytes = after - baseline,
                PreviewScratch = previewScratch,
                FullScratch = fullScratch,
                LargestPerRoleScratch = largest,
                LargestScratchBytes = largest.TotalBytes,
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
                    nameof(NoiseReduction_ReportsRetainedManagedBytes_WhenEnabled) + ".json"), json);
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

    // Independent payload calculation: no scratch inspection, pool rounding or
    // production scale resolver. Native dimensions are fixed by this workload.
    private static ScratchReport ScratchGeometry(int width, int height)
    {
        static int[] Supports(int scaleCount, int width, int height) =>
            Enumerable.Range(1, scaleCount)
                .Select(index => index + Math.Log2(width / 6000d))
                .Where(index => Math.Pow(2, index) >= 0.3)
                .Select(index => (int)Math.Round(index, MidpointRounding.AwayFromZero))
                .Where(index => index is >= 1 and <= 30)
                .Select(index => 1 << index)
                .Where(radius => radius <= Math.Min(width, height) / 4d).ToArray();

        var luma = Supports(4, width, height);
        var chroma = Supports(5, width, height);
        var alignment = 1 << Math.Max(0, chroma.Length - 1);
        var halo = Math.Max(luma.Sum(),
            (chroma.Sum() + alignment - 1) / alignment * alignment);
        const int corePixelLimit = 2_000_000;
        var rows = Math.Min(height, Math.Max(halo, corePixelLimit / width));
        if (rows < height) rows = Math.Max(halo, rows / alignment * alignment);
        var sourceRows = Math.Min(height, rows + 2 * halo);
        var planePixels = (long)sourceRows * width;
        return new ScratchReport(width, height, halo, rows, sourceRows,
            new ScratchRoles(planePixels * 3 * sizeof(ushort),
                planePixels * sizeof(float), planePixels * 2 * sizeof(float),
                planePixels * 2 * sizeof(float), (long)rows * width * 2 * sizeof(float)));
    }

    private static ScratchRoles LargestRoles(ScratchReport first, ScratchReport second) =>
        new(Math.Max(first.Roles.SourceBytes, second.Roles.SourceBytes),
            Math.Max(first.Roles.LumaBytes, second.Roles.LumaBytes),
            Math.Max(first.Roles.ChromaBytes, second.Roles.ChromaBytes),
            Math.Max(first.Roles.HorizontalBytes, second.Roles.HorizontalBytes),
            Math.Max(first.Roles.AdjustmentBytes, second.Roles.AdjustmentBytes));

    private sealed record ScratchReport(
        int Width, int Height, int Halo, int BandRows, int SourceRows, ScratchRoles Roles);

    private sealed record ScratchRoles(long SourceBytes, long LumaBytes,
        long ChromaBytes, long HorizontalBytes, long AdjustmentBytes)
    {
        public long TotalBytes =>
            SourceBytes + LumaBytes + ChromaBytes + HorizontalBytes + AdjustmentBytes;
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
                    if (!run.Wait(TestWaits.Condition)) throw new TimeoutException("NR worker not started.");
                    if (pairStart is not null && !pairStart.Wait(TestWaits.Condition))
                        throw new TimeoutException("Concurrent pair not released.");
                    Denoise(images, offset);
                }
                catch (Exception exception) { error = exception; }
                finally { done.Set(); }
                release.Wait(TestWaits.Condition);
            }) { IsBackground = true };
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Denoise(MagickImage[] images, int offset)
        {
            var info = new BaseImageInfo(BaseSourceKind.RawLibRaw, true, BaseDecodeSettings.Default,
                null, null, 6504, 0, false, null, 1, 6000, 4000);
            var settings = new DetailSettings { LuminanceNr = 50, ChromaNr = 50 };
            RenderNoiseReduction.Apply(images[offset], info, settings);
            RenderNoiseReduction.Apply(images[offset + 1], info, settings);
        }

        public void Start() => thread.Start();
        public void Run() => run.Set();
        public void WaitReady() => Assert.True(ready.Wait(TestWaits.Condition));
        public void WaitDone()
        {
            Assert.True(done.Wait(TestWaits.Condition), "NR worker did not finish.");
            Assert.Null(error);
        }

        public void Dispose()
        {
            run.Set();
            if (thread.IsAlive && !thread.Join(TestWaits.Condition))
                throw new TimeoutException("NR worker did not exit.");
            ready.Dispose();
            run.Dispose();
            done.Dispose();
        }
    }
}
