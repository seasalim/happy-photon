using System.Diagnostics;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class HealGateTests
{
    [WindowsFact]
    public async Task G5G6Refinement()
    {
        OptIn(); RequireWic();
        await Refinement(LocalFile(Raw), gated: true);
    }

    private async Task Refinement(ImageFile file, bool gated)
    {
        var loader = Loader(); var prototype = new HealProductionStage();
        using var folder = new TemporaryDirectory();
        using var catalog = new CatalogService(folder.Path); await catalog.InitializeAsync();
        await using var service = new PreviewService(catalog, loader, new RenderPipeline());
        // Today's Browse/Compare full-decode + Preview + bitmap path, same process/file.
        var controls = new double[gated ? 1 : 5];
        for (var sample = 0; sample < controls.Length; sample++)
        {
            var start = Stopwatch.GetTimestamp();
            using (var loupe = await service.LoadComparePreviewAsync(file, new EditSettings(), 10000))
                Assert.NotNull(loupe);
            controls[sample] = Stopwatch.GetElapsedTime(start).TotalSeconds;
        }
        var control = Median(controls);
        using var full = loader.LoadFullBase(file, BaseDecodeSettings.Default, CancellationToken.None);
        Assert.NotNull(full);
        using (RefinedBitmap(full, prototype)) { }
        var warm = new double[gated ? 1 : 5]; var cold = new double[warm.Length];
        for (var sample = 0; sample < warm.Length; sample++)
        {
            warm[sample] = Time(() => { using var bitmap = RefinedBitmap(full, prototype); }) / 1000;
            cold[sample] = Time(() =>
            {
                using var loaded = loader.LoadFullBase(file, BaseDecodeSettings.Default, CancellationToken.None);
                Assert.NotNull(loaded); using var bitmap = RefinedBitmap(loaded, prototype);
            }) / 1000;
        }
        Report(gated ? "G5G6" : "40MP-latency", new { source = file.FilePath, size = Size(full),
            workers = 2, controls, control, warm, cold, warmMedian = Median(warm), coldMedian = Median(cold),
            controlMin = 1.8, controlMax = 3.6, warmLimit = 1.5, coldLimit = control * 1.10,
            includesBitmap = true, extraBaseCopy = true, scratchBytes = prototype.RetainedScratchBytes });
        // Report-only arms run after all approved gate samples.
        RefinementDiagnostic(full, prototype, 2);
        RefinementDiagnostic(full, prototype, Environment.ProcessorCount);
    }

    private static Bitmap RefinedBitmap(BaseImage full, HealProductionStage prototype)
    {
        using var repaired = prototype.Repair(full, HealWorkloads.S64(), Candidate, 2);
        using var rendered = new RenderPipeline().RenderResting(new(repaired, HealWorkloads.LH8(), RenderIntent.Export,
            null, new(false, false)), RenderExecutionOptions.Resting(CancellationToken.None, 2));
        // Same BGRA buffer + native bitmap conversion used by today's Develop resting path.
        return BitmapConversionService.ConvertToBitmap(rendered.Image)!;
    }

    [WindowsFact]
    public void G7G8Memory()
    {
        OptIn(); RequireWic(); InstalledSurface(LocalFile(Raw), gated: true);
    }

    private void InstalledSurface(ImageFile file, bool gated)
    {
        var loader = Loader(); var outcome = loader.LoadPreviewBaseWithOutcome(file, BaseDecodeSettings.Default, CancellationToken.None);
        using var pair = outcome.Pair; Assert.NotNull(pair); Assert.NotNull(pair.Large);
        var prototype = new HealProductionStage();
        using var fit = FitBitmap(pair.Interactive, prototype);
        using var process = Process.GetCurrentProcess();
        process.Refresh(); var fitBytes = process.PrivateMemorySize64;
        var fitScratch = PipelineScratchBytes(); var fitSpotScratch = prototype.RetainedScratchBytes;
        using var full = loader.LoadFullBase(file, BaseDecodeSettings.Default, CancellationToken.None);
        Assert.NotNull(full);
        var displayed = RefinedBitmap(full, prototype);
        try
        {
            process.Refresh(); var idle = process.PrivateMemorySize64;
            var previous = displayed;
            using var peak = new BrushPrivateMemorySampler();
            try
            {
                var replacement = RefinedBitmap(full, prototype);
                // Installation transfers the owning reference before the previous bitmap is released.
                displayed = replacement;
                peak.Finish(); GC.KeepAlive(previous);
            }
            finally { previous.Dispose(); }
            var pixelCount = (long)full.Pixels.Width * full.Pixels.Height;
            Report(gated ? "G7G8" : "40MP-memory", new { source = file.FilePath, size = Size(full),
                interactive = Size(pair.Interactive), large = Size(pair.Large), backend = "WIC", forcedGc = false,
                fitBytes, idleBytes = idle, installedDelta = idle - fitBytes, installedLimit = 1.1 * pixelCount * 10,
                peakBytes = peak.Peak, peakDelta = peak.Peak - idle, peakLimit = 1.1 * pixelCount * 14,
                scratchBytes = prototype.RetainedScratchBytes, fitMin = 150000000, fitMax = 900000000 });
            // Gate idle and peak remain uncollected. Compact only after both samples.
            RetainedMemoryDiagnostic(full, displayed, prototype, fitBytes, idle, fitScratch, fitSpotScratch);
        }
        finally { displayed.Dispose(); }
        GC.KeepAlive(fit); GC.KeepAlive(outcome); GC.KeepAlive(pair); GC.KeepAlive(full); GC.KeepAlive(prototype);
    }

    private static Bitmap FitBitmap(BaseImage basis, HealProductionStage prototype)
    {
        using var repaired = prototype.Repair(basis, HealWorkloads.S64(), Candidate);
        using var rendered = new RenderPipeline().Render(new(repaired, HealWorkloads.LH8(), RenderIntent.Preview,
            1600, new(ComputeStats: true, ComputeOverlayMasks: false, ComputeHistogram: true, PreparePreviewPixels: true)));
        return BitmapConversionService.ConvertToBitmap(rendered.PreviewPixels!, (int)rendered.Image.Width, (int)rendered.Image.Height);
    }

    [WindowsFact]
    public async Task Record40Mp()
    {
        OptIn(); RequireWic();
        using var folder = new TemporaryDirectory();
        var raf = Environment.GetEnvironmentVariable("HAPPY_PHOTON_XT50_FIXTURE");
        string path;
        if (raf != null && File.Exists(raf))
        {
            Assert.Equal(SourceAvailability.AvailableLocally, new SourceAvailabilityService().GetAvailability(raf));
            path = raf;
        }
        else
        {
            path = Path.Combine(folder.Path, "heal-generated-7752x5178.jpg");
            using var generated = new MagickImage("gradient:", new MagickReadSettings { Width = 7752, Height = 5178 });
            generated.ColorSpace = ColorSpace.sRGB; generated.Quality = 92; generated.Write(path);
        }
        var file = new ImageFile(path);
        if (Environment.GetEnvironmentVariable("HAPPY_PHOTON_HEAL_40MP_ARM") == "memory")
            InstalledSurface(file, gated: false);
        else
            await Refinement(file, gated: false);
    }
}
