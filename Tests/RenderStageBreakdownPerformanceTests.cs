using System.Diagnostics;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

// Per-stage render cost, caller-thread whole-frame allocation, and GC churn at the
// interactive (1600 px) and resting (3200 px) preview sizes. Run each test in its
// own Release process; HAPPY_PHOTON_STAGE_REPORT_DIR also writes one JSON per test.
[Collection(AvaloniaTestCollection.Name)]
public sealed class RenderStageBreakdownPerformanceTests(
    AvaloniaTestFixture fixture,
    ITestOutputHelper output)
{
    private const int SampleCount = 7;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    [WindowsFact]
    public async Task InteractiveTick_ReportsStageCostAndGcChurn_WhenEnabled()
    {
        fixture.RequireWindows();
        RequirePerf();
        using var catalogRoot = new TemporaryDirectory();
        using var catalog = new CatalogService(catalogRoot.Path);
        await catalog.InitializeAsync();
        await using var service = new PreviewService(
            catalog,
            new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()),
            new RenderPipeline(),
            new PreviewCacheService(catalog),
            new RenderedThumbnailCacheService(catalog));
        var samples = new List<RenderStageSample>();
        using var listener = RenderStageProbe.Listen(sample =>
        {
            lock (samples) samples.Add(sample);
        });

        var cases = new List<CaseReport>();
        foreach (var (label, path) in Fixtures())
        {
            var file = new ImageFile(path);
            foreach (var (profile, create) in Profiles())
            {
                var ticks = new List<TickRecord>();
                for (var index = -1; index < SampleCount; index++)
                {
                    lock (samples) samples.Clear();
                    var tick = await MeasureTick(async () =>
                    {
                        var (preview, _) = await service.ApplyEditsToPreviewAsync(
                            file, create(index + 1), skipHistogram: false);
                        Assert.NotNull(preview);
                        preview.Dispose();
                    }, samples);
                    if (index >= 0) ticks.Add(tick);
                }
                cases.Add(Summarize(label, profile, 1600, ticks));
            }
        }

        Publish(nameof(InteractiveTick_ReportsStageCostAndGcChurn_WhenEnabled), cases);
        Assert.All(cases, item => Assert.Contains(item.Stages, stage => stage.Stage == "analysis"));
        Assert.All(cases.Where(item => item.Profile == "contrast"), item => Assert.True(
            item.MedianMs <= 150,
            $"{item.Fixture} contrast tick median {item.MedianMs:F1} ms; budget is 150 ms."));
    }

    [Fact]
    public async Task RestingRender_ReportsStageCostAndGcChurn_WhenEnabled()
    {
        RequirePerf();
        var loader = new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader());
        var pipeline = new RenderPipeline();
        var samples = new List<RenderStageSample>();
        using var listener = RenderStageProbe.Listen(sample =>
        {
            lock (samples) samples.Add(sample);
        });

        var cases = new List<CaseReport>();
        foreach (var (label, path) in Fixtures())
        {
            using var pair = LoadPair(loader, path);
            var large = pair.Large ?? throw new InvalidOperationException(
                $"{label} produced no large preview base.");
            foreach (var (profile, create) in Profiles())
            {
                var ticks = new List<TickRecord>();
                for (var index = -1; index < SampleCount; index++)
                {
                    lock (samples) samples.Clear();
                    var tick = await MeasureTick(() =>
                    {
                        using var rendered = pipeline.RenderResting(
                            new RenderRequest(
                                large,
                                create(index + 1),
                                RenderIntent.Preview,
                                BaseImage.LargePreviewMaxDimension,
                                new RenderOptions(ComputeStats: false, ComputeOverlayMasks: false)),
                            RenderExecutionOptions.Resting(CancellationToken.None));
                        return Task.CompletedTask;
                    }, samples);
                    if (index >= 0) ticks.Add(tick);
                }
                cases.Add(Summarize(label, profile, BaseImage.LargePreviewMaxDimension, ticks));
            }
        }

        Publish(nameof(RestingRender_ReportsStageCostAndGcChurn_WhenEnabled), cases);
        Assert.All(cases, item => Assert.Contains(item.Stages, stage => stage.Stage == "encode-target"));
    }

    [Fact]
    public void Q16RoundTrip_ReportsCopyCostAtPreviewSizes_WhenEnabled()
    {
        RequirePerf();
        var loader = new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader());
        using var pair = LoadPair(loader, GoldenTestPaths.Asset("canon-eos-6d-iso-6400.cr2"));
        var reports = new List<RoundTripReport>();
        foreach (var source in new[] { pair.Interactive.Pixels, pair.Large!.Pixels })
        {
            using var owned = new MagickImage(source);
            RoundTrip(owned);
            var ownedMs = Median(Repeat(() => RoundTrip(owned)));
            var cloneMs = Median(Repeat(() =>
            {
                using var clone = new MagickImage(source);
                RoundTrip(clone);
            }));
            var readMs = Median(Repeat(() =>
            {
                using var pixels = owned.GetPixelsUnsafe();
                _ = pixels.ToShortArray(PixelMapping.RGB);
            }));
            var frameBytes = (long)owned.Width * owned.Height * owned.ChannelCount * sizeof(ushort);
            reports.Add(new RoundTripReport(
                checked((int)owned.Width), checked((int)owned.Height), frameBytes,
                ownedMs, cloneMs, readMs));
        }

        foreach (var report in reports)
        {
            output.WriteLine(
                $"roundtrip size={report.Width}x{report.Height} frameMiB={Mib(report.FrameBytes):F1} " +
                $"ownedMs={report.OwnedRoundTripMs:F2} cloneThenRoundTripMs={report.CloneRoundTripMs:F2} " +
                $"readOnlyCopyMs={report.ReadOnlyCopyMs:F2}");
        }
        WriteReport(nameof(Q16RoundTrip_ReportsCopyCostAtPreviewSizes_WhenEnabled), reports);
        Assert.Equal(2, reports.Count);
    }

    [Fact]
    public void PreviewBaseLoad_ReportsColdAndWarmDecodeCost_WhenEnabled()
    {
        RequirePerf();
        var loader = new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader());
        var reports = new List<BaseLoadMeasurement.Report>();
        foreach (var (label, path) in Fixtures())
            reports.Add(BaseLoadMeasurement.Measure(label, () => LoadPair(loader, path), output));
        WriteReport(nameof(PreviewBaseLoad_ReportsColdAndWarmDecodeCost_WhenEnabled), reports);
        Assert.Equal(3, reports.Count);
    }

    private static void RequirePerf()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to run render stage breakdown diagnostics.");
        PerfEnvironment.AssertFullCpu();
    }

    private static IEnumerable<(string Label, string Path)> Fixtures() =>
    [
        ("canon-6d", GoldenTestPaths.Asset("canon-eos-6d-iso-6400.cr2")),
        ("fuji-x30", GoldenTestPaths.Asset("fujifilm-x30.raf")),
        ("jpeg-24mp", CullPerfFiles.GeneratedJpeg())
    ];

    // "contrast" is the standing 150 ms slider tick; "chroma" isolates the
    // saturation/vibrance/mixer stage; "edited" adds exposure, luminance NR, and
    // effects so every whole-frame stage pays its copy.
    private static IEnumerable<(string Name, Func<int, EditSettings> Create)> Profiles() =>
    [
        ("contrast", index => new EditSettings { Contrast = 20 + index }),
        ("chroma", index =>
        {
            var settings = new EditSettings
            {
                Contrast = 20 + index,
                Saturation = 20,
                Vibrance = 15,
                Mixer = new ColorMixerSettings()
            };
            settings.Mixer.Orange.Hue = 20;
            return settings;
        }),
        ("edited", index =>
        {
            var settings = new EditSettings
            {
                Exposure = 0.3,
                Contrast = 20 + index,
                Saturation = 20,
                Vibrance = 15,
                Mixer = new ColorMixerSettings(),
                Detail = new DetailSettings { LuminanceNr = 50 },
                Effects = new EffectsSettings { Vignette = -20 }
            };
            settings.Mixer.Orange.Hue = 20;
            return settings;
        })
    ];

    private static PreviewBasePair LoadPair(IBaseImageLoader loader, string path) =>
        loader.LoadPreviewBaseWithOutcome(
            new ImageFile(path),
            BaseDecodeSettings.Default,
            CancellationToken.None).Pair ??
        throw new InvalidOperationException($"Fixture did not decode: {path}.");

    private static async Task<TickRecord> MeasureTick(
        Func<Task> render,
        List<RenderStageSample> samples)
    {
        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);
        var pause = GC.GetTotalPauseDuration();
        var allocated = GC.GetTotalAllocatedBytes(precise: true);
        var stopwatch = Stopwatch.StartNew();
        await render();
        stopwatch.Stop();
        var allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
        RenderStageSample[] stages;
        lock (samples) stages = samples.ToArray();
        return new TickRecord(
            stopwatch.Elapsed.TotalMilliseconds,
            allocatedAfter - allocated,
            GC.CollectionCount(0) - gen0,
            GC.CollectionCount(1) - gen1,
            GC.CollectionCount(2) - gen2,
            (GC.GetTotalPauseDuration() - pause).TotalMilliseconds,
            stages);
    }

    private static CaseReport Summarize(
        string fixtureLabel,
        string profile,
        int size,
        List<TickRecord> ticks)
    {
        var stages = ticks.SelectMany(tick => tick.Stages)
            .GroupBy(sample => sample.Stage)
            .Select(group => new StageReport(
                group.Key,
                Median(group.Select(sample => sample.Elapsed.TotalMilliseconds)),
                (long)Median(group.Select(sample => (double)sample.CallerAllocatedBytes)),
                group.Last().FrameBytes,
                $"{group.Last().Width}x{group.Last().Height}"))
            .ToArray();
        return new CaseReport(
            fixtureLabel,
            profile,
            size,
            Median(ticks.Select(tick => tick.ElapsedMs)),
            ticks.Max(tick => tick.ElapsedMs),
            (long)Median(ticks.Select(tick => (double)tick.AllocatedBytes)),
            ticks.Sum(tick => tick.Gen0),
            ticks.Sum(tick => tick.Gen1),
            ticks.Sum(tick => tick.Gen2),
            ticks.Sum(tick => tick.PauseMs),
            ticks.Count,
            stages);
    }

    private void Publish(string name, List<CaseReport> cases)
    {
        foreach (var item in cases)
        {
            output.WriteLine(
                $"case fixture={item.Fixture} profile={item.Profile} size={item.Size} " +
                $"medianMs={item.MedianMs:F1} maxMs={item.MaxMs:F1} " +
                $"allocMiB={Mib(item.MedianAllocatedBytes):F1} gc0/1/2={item.Gen0}/{item.Gen1}/{item.Gen2} " +
                $"pauseMs={item.PauseMs:F1} samples={item.Samples}");
            foreach (var stage in item.Stages)
            {
                output.WriteLine(
                    $"  stage={stage.Stage} ms={stage.MedianMs:F2} callerAllocMiB={Mib(stage.MedianCallerAllocatedBytes):F1} " +
                    $"frameMiB={Mib(stage.FrameBytes):F1} frame={stage.FrameSize}");
            }
        }
        WriteReport(name, cases);
    }

    private static void WriteReport(string name, object value)
    {
        if (Environment.GetEnvironmentVariable("HAPPY_PHOTON_STAGE_REPORT_DIR") is not { Length: > 0 } directory)
            return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, name + ".json"),
            JsonSerializer.Serialize(value, Json) + Environment.NewLine);
    }

    private static void RoundTrip(MagickImage image)
    {
        using var pixels = image.GetPixels();
        var values = pixels.GetArea(0, 0, image.Width, image.Height) ??
            throw new InvalidOperationException("Unable to access Q16 pixels.");
        pixels.SetArea(0, 0, image.Width, image.Height, values);
    }

    private static double[] Repeat(Action action)
    {
        action();
        var samples = new double[9];
        for (var index = 0; index < samples.Length; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            action();
            samples[index] = stopwatch.Elapsed.TotalMilliseconds;
        }
        return samples;
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.Order().ToArray();
        return ordered.Length == 0 ? 0 : ordered[ordered.Length / 2];
    }

    private static double Mib(long bytes) => bytes / 1024d / 1024d;

    private sealed record TickRecord(
        double ElapsedMs,
        long AllocatedBytes,
        int Gen0,
        int Gen1,
        int Gen2,
        double PauseMs,
        RenderStageSample[] Stages);

    private sealed record StageReport(
        string Stage,
        double MedianMs,
        long MedianCallerAllocatedBytes,
        long FrameBytes,
        string FrameSize);

    private sealed record CaseReport(
        string Fixture,
        string Profile,
        int Size,
        double MedianMs,
        double MaxMs,
        long MedianAllocatedBytes,
        int Gen0,
        int Gen1,
        int Gen2,
        double PauseMs,
        int Samples,
        StageReport[] Stages);

    private sealed record RoundTripReport(
        int Width,
        int Height,
        long FrameBytes,
        double OwnedRoundTripMs,
        double CloneRoundTripMs,
        double ReadOnlyCopyMs);
}
