using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class HealGateTests
{
    [WindowsFact]
    public async Task ReleaseOnlyCollectionDiagnostic()
    {
        OptIn();
        RequireWic();
        var loader = Loader();
        var file = LocalFile(Raw);
        var settings = HealWorkloads.LH8();
        settings.Repairs = HealWorkloads.Repairs(HealWorkloads.S64());
        using var pair = loader.LoadPreviewBaseWithOutcome(
            file, BaseDecodeSettings.Default, CancellationToken.None).Pair;
        Assert.NotNull(pair);
        using var fit = ProductionFitBitmap(pair.Interactive, settings);
        var fitBytes = PrivateBytes();

        // A lower-level feasibility probe, not the WP5 production installation gate.
        // No attribution logging, heap walking, or collection while the base is held.
        var held = MeasureHeldProductionBase(loader, file, settings);

        // Simulate the owner's release-only policy after the helper has returned:
        // its full base, both display bitmaps and render locals are now out of scope.
        var collectionMs = await Task.Run(() => Time(() =>
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive,
                blocking: true, compacting: true)));
        // test-wait-policy: allow - WP5 G7 samples five seconds after release.
        await Task.Delay(TimeSpan.FromSeconds(5));
        var releasedBytes = PrivateBytes();
        var idleLimit = 1.1 * held.Pixels * 12;
        var peakLimit = 1.1 * held.Pixels * 14;
        Report("wp5-release-only-feasibility", new
        {
            diagnostic = true,
            productionInstallation = false,
            backend = "WIC",
            workers = Environment.ProcessorCount,
            idleSampleDelaySeconds = 5,
            fitBytes,
            baselineValid = fitBytes is >= 150000000 and <= 900000000,
            firstIdleBytes = held.FirstIdle,
            firstIdleDelta = held.FirstIdle - fitBytes,
            warmIdleBytes = held.WarmIdle,
            warmIdleDelta = held.WarmIdle - fitBytes,
            idleLimit,
            firstIdleWithinG3 = held.FirstIdle - fitBytes <= idleLimit,
            warmIdleWithinG3 = held.WarmIdle - fitBytes <= idleLimit,
            peakBytes = held.Peak,
            peakDelta = held.Peak - held.FirstIdle,
            peakLimit,
            releasedBytes,
            releasedDelta = releasedBytes - fitBytes,
            releaseLimit = 50000000,
            releasedWithinG7 = releasedBytes - fitBytes <= 50000000,
            collectionMs,
            collectionPolicy = "one aggressive collection after full-base release only",
            pipelineScratch = PipelineScratchBytes()
        });
        GC.KeepAlive(fit);
        GC.KeepAlive(pair);
    }

    private static Bitmap ProductionFitBitmap(BaseImage basis, EditSettings settings)
    {
        using var rendered = new RenderPipeline().Render(new(
            basis, settings, RenderIntent.Preview, 1600,
            new(ComputeStats: true, ComputeOverlayMasks: false,
                ComputeHistogram: true, PreparePreviewPixels: true)));
        return BitmapConversionService.ConvertToBitmap(rendered.PreviewPixels!,
            (int)rendered.Image.Width, (int)rendered.Image.Height);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (long Pixels, long FirstIdle, long WarmIdle, long Peak)
        MeasureHeldProductionBase(IBaseImageLoader loader, ImageFile file, EditSettings settings)
    {
        using var full = loader.LoadFullBase(file, BaseDecodeSettings.Default, CancellationToken.None);
        Assert.NotNull(full);
        var displayed = ProductionRefinedBitmap(full, settings);
        try
        {
            // test-wait-policy: allow - report-only settled private-memory sample, not a synchronization wait.
            Thread.Sleep(TimeSpan.FromSeconds(5));
            var firstIdle = PrivateBytes();
            long peakBytes;
            using (var peak = new BrushPrivateMemorySampler())
            {
                var previous = displayed;
                displayed = ProductionRefinedBitmap(full, settings);
                peak.Finish();
                peakBytes = peak.Peak;
                previous.Dispose();
            }
            // test-wait-policy: allow - report-only settled private-memory sample, not a synchronization wait.
            Thread.Sleep(TimeSpan.FromSeconds(5));
            var warmIdle = PrivateBytes();
            GC.KeepAlive(displayed);
            return ((long)full.Pixels.Width * full.Pixels.Height,
                firstIdle, warmIdle, peakBytes);
        }
        finally
        {
            displayed.Dispose();
        }
    }

    private static Bitmap ProductionRefinedBitmap(BaseImage full, EditSettings settings)
    {
        using var rendered = new RenderPipeline().RenderResting(new(
            full, settings, RenderIntent.Export, null, new(false, false)),
            RenderExecutionOptions.Resting(CancellationToken.None, Environment.ProcessorCount));
        return BitmapConversionService.ConvertToBitmap(rendered.Image)!;
    }

    private static long PrivateBytes()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return process.PrivateMemorySize64;
    }
}
