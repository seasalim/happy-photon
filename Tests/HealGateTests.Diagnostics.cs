using System.Diagnostics;
using System.Reflection;
using System.Runtime;
using Avalonia.Media.Imaging;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class HealGateTests
{
    [Fact]
    public void RepairCostDiagnostic()
    {
        OptIn();
        var loader = Loader(); var file = LocalFile();
        using var pair = loader.LoadPreviewBaseWithOutcome(file, BaseDecodeSettings.Default, CancellationToken.None).Pair;
        Assert.NotNull(pair); Assert.NotNull(pair.Large);
        using var full = loader.LoadFullBase(file, BaseDecodeSettings.Default, CancellationToken.None);
        Assert.NotNull(full);
        foreach (var basis in new[] { pair.Interactive, pair.Large, full })
            RepairCost(basis, HealWorkloads.S64(), "S64");
        RepairCost(pair.Interactive, HealWorkloads.SCap((int)pair.Interactive.Pixels.Width,
            (int)pair.Interactive.Pixels.Height), "SCap");
    }

    private void RepairCost(BaseImage basis, HealSpot[] spots, string workload)
    {
        var candidates = new[] { new HealCandidate(HealFormulation.Membrane, HealDomain.Additive),
            new HealCandidate(HealFormulation.Gaussian, HealDomain.Additive) };
        var prototypes = new[] { new HealPrototype(), new HealPrototype() };
        var samples = new[] { new double[7], new double[7] };
        // Alternate order; every sample starts from the same pixels, with native COW
        // detachment completed outside the stopwatch. Warm each candidate twice.
        for (var sample = -2; sample < 7; sample++)
        for (var step = 0; step < 2; step++)
        {
            var arm = (sample + 2 + step) % 2;
            using var copy = DetachedCopy(basis);
            var elapsed = Time(() => prototypes[arm].Apply(copy, spots, candidates[arm], 2));
            if (sample >= 0) samples[arm][sample] = elapsed;
        }
        for (var arm = 0; arm < 2; arm++)
            Report("repair-cost-diagnostic", new { diagnostic = true, formulation = candidates[arm].ToString(),
                workload, size = Size(basis), workers = 2, warmup = 2, samples = samples[arm],
                medianMs = Median(samples[arm]), includesBaseCopy = false });
    }

    internal static MagickImage DetachedCopy(BaseImage basis)
    {
        var copy = new MagickImage(basis.Pixels);
        try
        {
            using var pixels = copy.GetPixels();
            var first = pixels.GetArea(0, 0, 1, 1)!;
            copy.ImportPixels(first, new PixelImportSettings(0, 0, 1, 1, StorageType.Quantum, PixelMapping.RGB));
            return copy;
        }
        catch { copy.Dispose(); throw; }
    }

    private void RefinementDiagnostic(BaseImage full, HealPrototype prototype, int workers)
    {
        var spots = HealWorkloads.S64(); var settings = HealWorkloads.LH8();
        var stages = new Dictionary<string, double>();
        double copyMs, repairMs, renderMs, bgraMs, bitmapMs;
        var totalStart = Stopwatch.GetTimestamp();
        var start = Stopwatch.GetTimestamp();
        using (var repaired = new BaseImage(DetachedCopy(full), full.Info))
        {
            copyMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            repairMs = Time(() => prototype.Apply(repaired.Pixels, spots, Candidate, workers));
            var stage = "setup"; var stageStart = Stopwatch.GetTimestamp();
            void Boundary(string next)
            {
                var now = Stopwatch.GetTimestamp();
                stages[stage] = stages.GetValueOrDefault(stage) + Stopwatch.GetElapsedTime(stageStart, now).TotalMilliseconds;
                stage = next; stageStart = now;
            }
            start = Stopwatch.GetTimestamp();
            using var rendered = new RenderPipeline().RenderResting(new(repaired, settings, RenderIntent.Export,
                null, new(false, false)), RenderExecutionOptions.Resting(CancellationToken.None, workers, Boundary));
            renderMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Boundary("complete");
            start = Stopwatch.GetTimestamp();
            var bgra = BitmapConversionService.CopyBgraPixels(rendered.Image);
            bgraMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            start = Stopwatch.GetTimestamp();
            using var bitmap = BitmapConversionService.ConvertToBitmap(bgra, (int)rendered.Image.Width, (int)rendered.Image.Height);
            bitmapMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        var totalMs = Stopwatch.GetElapsedTime(totalStart).TotalMilliseconds;
        Report("refinement-stage-diagnostic", new { diagnostic = true, size = Size(full), workers,
            workerPolicy = workers == 2 ? "two-workers" : "all-logical-processors",
            copyMs, repairMs, renderMs, stages, bgraMs, bitmapMs, totalMs,
            overheadAndDisposalMs = totalMs - copyMs - repairMs - renderMs - bgraMs - bitmapMs,
            stageMeaning = "elapsed between stage-start callbacks; includes intervening setup and finalization" });
    }

    [WindowsFact]
    public void DiagnosticProbesPreserveSourceAndDisplayedBitmap()
    {
        RequireWic();
        var input = Enumerable.Range(0, 160 * 120 * 3).Select(i => (ushort)(1000 + i % 30000)).ToArray();
        using var basis = RenderPipelineTestSupport.CreateBase(input, isRaw: true, height: 120);
        var prototype = new HealPrototype();
        using var bitmap = RefinedBitmap(basis, prototype);
        var before = BitmapConversionService.CopyBgraPixels(bitmap);
        RefinementDiagnostic(basis, prototype, 2);
        RefinementDiagnostic(basis, prototype, Environment.ProcessorCount);
        var scratch = PipelineScratchBytes();
        Assert.Equal(7, scratch.Count);
        Assert.All(scratch.Values, bytes => Assert.True(bytes >= 0));
        RetainedMemoryDiagnostic(basis, bitmap, prototype, 0, 0, scratch, prototype.RetainedScratchBytes);
        Assert.Equal(before, BitmapConversionService.CopyBgraPixels(bitmap));
        Assert.Equal(input, RenderPipelineTestSupport.ReadPixels(basis.Pixels));
    }

    private static Dictionary<string, long> PipelineScratchBytes()
    {
        var result = new Dictionary<string, long>();
        foreach (var owner in new[] { typeof(RenderNoiseReduction), typeof(RenderSharpening), typeof(RenderChromaStage) })
        foreach (var field in owner.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
        {
            if (!field.FieldType.IsGenericType || field.FieldType.GetGenericTypeDefinition() != typeof(RenderScratchSlot<>))
                continue;
            // Read-only test probe: do not Take/Return or change pipeline retention.
            var retained = field.FieldType.GetField("_scratch", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Update the HEAL scratch diagnostic for RenderScratchSlot's layout.");
            var array = retained.GetValue(field.GetValue(null)) as Array;
            result[$"{owner.Name}.{field.Name}"] = array == null ? 0 : Buffer.ByteLength(array);
        }
        return result;
    }

    private void RetainedMemoryDiagnostic(BaseImage full, Bitmap displayed, HealPrototype prototype,
        long fitBytes, long gatedIdle, Dictionary<string, long> fitScratch, long fitSpotScratch)
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh(); var beforeGc = process.PrivateMemorySize64;
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        process.Refresh(); var afterGc = process.PrivateMemorySize64;
        var heap = GC.GetGCMemoryInfo();
        var managedLiveBytes = GC.GetTotalMemory(false);
        var scratch = PipelineScratchBytes();
        var baseBytes = (long)full.Pixels.Width * full.Pixels.Height * 6;
        var bitmapBytes = (long)displayed.PixelSize.Width * displayed.PixelSize.Height * 4;
        var scratchBytes = scratch.Values.Sum() + prototype.RetainedScratchBytes;
        var scratchGrowth = scratchBytes - fitScratch.Values.Sum() - fitSpotScratch;
        Report("retained-memory-diagnostic", new { diagnostic = true, forcedGc = true, lohCompacted = true,
            fitBytes, gatedIdle, beforeGc, afterGc, privateReduction = beforeGc - afterGc,
            managedLiveBytes, managedHeapBytes = heap.HeapSizeBytes, managedCommittedBytes = heap.TotalCommittedBytes,
            managedFragmentedBytes = heap.FragmentedBytes, baseBytes, bitmapBytes,
            pipelineScratch = scratch, fitPipelineScratch = fitScratch, fitSpotScratch,
            prototypeScratchBytes = prototype.RetainedScratchBytes, retainedScratchBytes = scratchBytes, scratchGrowth,
            privateDeltaOverUncollectedFit = afterGc - fitBytes,
            remainderBytes = afterGc - fitBytes - baseBytes - bitmapBytes - scratchGrowth,
            attribution = "pixel/array payloads exclude headers; remainder includes native/GC commitment and uncollected Fit baseline" });
        GC.KeepAlive(full); GC.KeepAlive(displayed); GC.KeepAlive(prototype);
    }
}
