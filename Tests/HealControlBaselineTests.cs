using System.Diagnostics;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

// HEAL-WP1 controls only; observations do not approve the proposed envelopes.
// One method per fresh Release process through RunHealControls.ps1.
[Collection(AvaloniaTestCollection.Name)]
public sealed class HealControlBaselineTests(ITestOutputHelper output)
{
    private const string Raw = "canon-eos-6d-iso-6400.cr2";
    private const string Heic = "iphone-14-pro-iso-1000.heic";

    [Fact]
    public void C1PreviewParity()
    {
        OptIn();
        var fixture = Environment.GetEnvironmentVariable("HAPPY_PHOTON_HEAL_FIXTURE") == "standard" ? Heic : Raw;
        var file = LocalFile(fixture);
        var loader = Loader();
        // Real preview decode, independent of and before the full decode.
        using var pair = loader.LoadPreviewBaseWithOutcome(file, BaseDecodeSettings.Default,
            CancellationToken.None).Pair;
        Assert.NotNull(pair);
        Assert.NotNull(pair.Large);
        using var full = loader.LoadFullBase(file, BaseDecodeSettings.Default, CancellationToken.None);
        Assert.NotNull(full);
        var pipeline = new RenderPipeline();
        var settings = new EditSettings();
        using var reference = pipeline.Render(new(full, settings, RenderIntent.Export, null, new(false, false)));
        foreach (var previewBase in new[] { pair.Interactive, pair.Large })
        {
            // Diagnostic only: HAPPY_PHOTON_HEAL_C1_INTENT=export separates decode gap from intent.
            var intent = Environment.GetEnvironmentVariable("HAPPY_PHOTON_HEAL_C1_INTENT") == "export"
                ? RenderIntent.Export : RenderIntent.Preview;
            using var preview = pipeline.Render(new(previewBase, settings, intent,
                null, new(false, false)));
            using var aligned = new MagickImage(reference.Image);
            // Match GoldenRenderTests' display-sRGB alignment and comparer at actual pair dimensions.
            WysiwygTests.AlignForComparison(aligned, preview.Image);
            var metric = GoldenImageComparer.Compare(aligned, preview.Image, GoldenComparisonDomain.DisplaySrgb);
            Print($"C1 fixture={fixture} base={Size(previewBase)} full={Size(full)} " +
                $"provenance={(fixture == Raw ? "RawBaseLoader-half-decode-pair" : "StandardBaseLoader-preview-pair")} " +
                $"resized_from_full=False preview_intent={intent} mean_dE={metric.MeanDeltaE:R} p99_dE={metric.P99DeltaE:R} " +
                $"samples=1 within_envelope={metric.MeanDeltaE <= 2 && metric.P99DeltaE <= 8}");
        }
    }

    [Fact]
    public async Task C2ProofExportParity()
    {
        OptIn();
        var file = LocalFile(Raw);
        using var directory = new TemporaryDirectory();
        var pipeline = new RenderPipeline();
        var calls = 0;
        long differing = -1;
        var service = new ImageExportService(pipeline, Loader(), new ExportMetadataService(),
            new DcpProfileService(new SourceAvailabilityService()), request =>
            {
                calls++;
                Assert.Equal(RenderIntent.Export, request.Intent);
                Assert.Null(request.MaxDimension);
                Assert.True(request.Settings.Detail.ResolveCaptureSharpen(true) > 0);
                // Independent Proof-style render of the exact full base supplied by export's hook.
                var proofUpstream = pipeline.RenderDisplayRec2020(new(request.Base, request.Settings,
                    RenderIntent.Export, null, new(false, false)));
                using var proof = RenderFinalizer.FinalizeOwnedProof(proofUpstream, null,
                    OutputColorSpace.Srgb, OutputSharpeningMode.Off, request.Settings.Effects);
                var exportUpstream = pipeline.RenderDisplayRec2020(request);
                try
                {
                    // Exact production full-size export finalizer; compare before encoding, not JPEG bytes.
                    using var canonical = RenderFinalizer.Finalize(exportUpstream, null,
                        OutputColorSpace.Srgb, OutputSharpeningMode.Off, wasResized: false,
                        effects: request.Settings.Effects);
                    Assert.Equal(proof.Width, canonical.Width);
                    Assert.Equal(proof.Height, canonical.Height);
                    using var a = proof.GetPixelsUnsafe();
                    using var b = canonical.GetPixelsUnsafe();
                    var left = a.ToShortArray(PixelMapping.RGB)!;
                    var right = b.ToShortArray(PixelMapping.RGB)!;
                    Assert.Equal(left.Length, right.Length);
                    differing = 0;
                    for (var index = 0; index < left.Length; index++)
                        if (left[index] != right[index]) differing++;
                    Print($"C2 fixture={Raw} base={Size(request.Base)} compared={proof.Width}x{proof.Height} " +
                        $"capture_sharpen={request.Settings.Detail.ResolveCaptureSharpen(true)} output_sharpen=Off " +
                        $"differing_Q16_codes={differing} codes={left.Length} samples=1 same_base=True");
                    return exportUpstream;
                }
                catch { exportUpstream.Dispose(); throw; }
            });
        var result = await service.ExportBatchAsync([file], new ExportSettings
        {
            OutputFolder = directory.Path, Format = ExportFormat.Jpeg,
            ExportHiRes = true, ExportWeb = false, ExportSmall = false,
            OutputColorSpace = OutputColorSpace.Srgb, OutputSharpening = OutputSharpeningMode.Off
        });
        Assert.True(result.ExportedCount == 1, string.Join(';', result.FailedTargets.Select(t => t.FailureReason)));
        Assert.Equal(1, calls);
        Assert.Equal(0, differing);
    }

    [WindowsFact]
    public async Task C3LoupeRefinement()
    {
        OptIn(); RequireWic();
        var file = LocalFile(Raw);
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        await using var service = new PreviewService(catalog, Loader(), new RenderPipeline());
        const int requiredLongEdge = 5496;
        Assert.True(requiredLongEdge > BaseImage.LargePreviewMaxDimension);
        var start = Stopwatch.GetTimestamp();
        using var result = await service.LoadComparePreviewAsync(file, new EditSettings(), requiredLongEdge);
        var seconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
        Assert.NotNull(result);
        Assert.Equal(result.OriginalViewPixelSize, result.Bitmap.PixelSize);
        Print($"C3 fixture={Raw} dimensions={result.Bitmap.PixelSize.Width}x{result.Bitmap.PixelSize.Height} " +
            $"required_long_edge={requiredLongEdge} seconds={seconds:R} samples=1 " +
            "path=LoadComparePreviewAsync-full-decode-Preview-render-bitmap warmup=none");
    }

    [WindowsFact]
    public void C4FitPrivateBytes()
    {
        OptIn(); RequireWic();
        var file = LocalFile(Raw);
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var before = process.PrivateMemorySize64;
        var outcome = Loader().LoadPreviewBaseWithOutcome(file, BaseDecodeSettings.Default, CancellationToken.None);
        using var pair = outcome.Pair;
        Assert.NotNull(pair);
        Assert.NotNull(pair.Large);
        using var displayedFit = CreateDevelopFitBitmap(pair.Interactive);
        // RenderResult is disposed; retain real pair, analysis and display bitmap at Fit idle.
        // No forced GC: sample absolute commitment like BrushPrivateMemorySampler.Baseline.
        process.Refresh();
        var fit = process.PrivateMemorySize64;
        Print($"C4 fixture={Raw} full={pair.Interactive.Info.FullWidth}x{pair.Interactive.Info.FullHeight} " +
            $"interactive={Size(pair.Interactive)} large={Size(pair.Large)} " +
            $"displayed={displayedFit.PixelSize.Width}x{displayedFit.PixelSize.Height} " +
            $"before_private_bytes={before} fit_private_bytes={fit} fit_MiB={fit / 1048576.0:R} " +
            $"fit_MB={fit / 1000000.0:R} samples=1 forced_gc=False backend=WIC");
        GC.KeepAlive(outcome);
        GC.KeepAlive(pair);
        GC.KeepAlive(displayedFit);
    }

    private static Bitmap CreateDevelopFitBitmap(BaseImage source)
    {
        using var rendered = new RenderPipeline().Render(new(source, new EditSettings(), RenderIntent.Preview,
            BaseImage.InteractivePreviewMaxDimension,
            new(ComputeStats: true, ComputeOverlayMasks: false, ComputeHistogram: true, PreparePreviewPixels: true)));
        Assert.NotNull(rendered.PreviewPixels);
        return BitmapConversionService.ConvertToBitmap(rendered.PreviewPixels,
            (int)rendered.Image.Width, (int)rendered.Image.Height);
    }

    private static void RequireWic()
    {
        Assert.NotEqual("1", Environment.GetEnvironmentVariable("HAPPY_PHOTON_TEST_SKIA_ONLY"));
        Assert.True(OperatingSystem.IsWindows());
        // AvaloniaPlatformAssemblyFixture initializes native Windows/WIC unless SKIA_ONLY is set.
    }

    private static ImageFile LocalFile(string fixture)
    {
        var path = GoldenTestPaths.Asset(fixture);
        Assert.Equal(SourceAvailability.AvailableLocally, new SourceAvailabilityService().GetAvailability(path));
        return new ImageFile(path);
    }

    private static IBaseImageLoader Loader() => new GatedBaseImageLoader(
        new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()), new SourceAvailabilityService());
    private static string Size(BaseImage image) => $"{image.Pixels.Width}x{image.Pixels.Height}";
    private void Print(string text) => output.WriteLine(
        $"HEAL_CONTROL {text} pid={Environment.ProcessId} cpu={Environment.ProcessorCount}");
    private static void OptIn()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1", "Opt-in HEAL controls");
        Assert.Equal("1", Environment.GetEnvironmentVariable("HAPPY_PHOTON_FULL_CPU"));
        PerfEnvironment.AssertFullCpu();
#if DEBUG
        Assert.Fail("Use Release");
#endif
    }
}
