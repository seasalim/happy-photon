using System.Diagnostics;
using System.Text.Json;
using FontManager = Avalonia.Media.FontManager;
using ImageMagick;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

// Serialize the global probe listener with the existing stage diagnostics.
[Collection(AvaloniaTestCollection.Name)]
public sealed class FullSizeExportPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task FullSizeJpeg_ReportsTotalAndStageCost_WhenEnabled()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to measure full-size exports.");
        PerfEnvironment.AssertFullCpu();
        // Harness validation only: one export, no warm-up, explicitly not a baseline.
        var watermarkOn = Environment.GetEnvironmentVariable("HAPPY_PHOTON_WATERMARK") == "1";
        var caseName = watermarkOn ? "watermark-on" : "watermark-absent";
        var smoke = Environment.GetEnvironmentVariable("HAPPY_PHOTON_EXPORT_SMOKE") == "1";
        var sampleCount = smoke ? 1 : 7;
        var warmups = smoke ? 0 : 1;
        var file = new ImageFile(CullPerfFiles.GeneratedJpeg());
        using var root = new TemporaryDirectory();
        var loader = new GatedBaseImageLoader(
            new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()),
            new SourceAvailabilityService());
        var service = new ImageExportService(new RenderPipeline(), loader, new ExportMetadataService());
        var stageSamples = new List<RenderStageSample>();
        using var listener = RenderStageProbe.Listen(sample =>
        {
            lock (stageSamples) stageSamples.Add(sample);
        });
        var ticks = new List<Tick>();
        for (var index = -warmups; index < sampleCount; index++)
        {
            var settings = new ExportSettings
            {
                OutputFolder = Path.Combine(root.Path, $"sample-{index}"),
                Format = ExportFormat.Jpeg, Quality = 85,
                OutputColorSpace = OutputColorSpace.Srgb,
                OutputSharpening = OutputSharpeningMode.Screen,
                ExportHiRes = true, ExportWeb = false, ExportSmall = false
            };
            ConfigureCase(settings, watermarkOn);
            var job = settings.CreateJob([file]);
            lock (stageSamples) stageSamples.Clear();
            var stopwatch = Stopwatch.StartNew();
            var result = await service.ExportBatchAsync(job);
            stopwatch.Stop();
            Assert.False(result.Stopped);
            Assert.True(result.SuccessfulTargetCount == 1,
                string.Join("; ", result.FailedTargets.Select(target => target.FailureReason)));
            Assert.True(new FileInfo(Assert.Single(result.Outcomes).ResolvedPath).Length > 0);
            RenderStageSample[] stages;
            lock (stageSamples) stages = stageSamples.ToArray();
            Assert.Contains(stages, sample => sample.Stage == "encode-target" &&
                                             sample.Width == 6000 && sample.Height == 4000);
            if (watermarkOn)
            {
                Assert.Contains(stages, sample => sample.Stage == "watermark");
                AssertChangedPixels(loader, file, settings);
            }
            else Assert.DoesNotContain(stages, sample => sample.Stage == "watermark");
            if (index >= 0) ticks.Add(new Tick(stopwatch.Elapsed.TotalMilliseconds,
                stages.GroupBy(sample => sample.Stage).ToDictionary(group => group.Key,
                    group => group.Sum(sample => sample.Elapsed.TotalMilliseconds))));
        }
        if (watermarkOn && !smoke)
            Assert.True(Median(ticks.Select(tick => tick.Stages["watermark"])) <= 15);
        var stageNames = ticks.SelectMany(tick => tick.Stages.Keys).Distinct().Order().ToArray();
        var report = new
        {
            Case = caseName, Smoke = smoke, Fixture = "jpeg-24mp",
            Width = 6000, Height = 4000, Quality = 85, ProcessorCount = Environment.ProcessorCount,
            Warmups = warmups, Samples = ticks.Count,
            MedianMs = Median(ticks.Select(tick => tick.TotalMs)),
            MinMs = ticks.Min(tick => tick.TotalMs), MaxMs = ticks.Max(tick => tick.TotalMs),
            Stages = stageNames.Select(name => new
            {
                Stage = name,
                MedianMs = Median(ticks.Select(tick => tick.Stages.GetValueOrDefault(name)))
            }).ToArray(),
            Ticks = ticks
        };
        output.WriteLine($"smoke={smoke} warmups={warmups} samples={ticks.Count} " +
            $"total median/min/max={report.MedianMs:F3}/{report.MinMs:F3}/{report.MaxMs:F3} ms");
        foreach (var stage in report.Stages)
            output.WriteLine($"stage={stage.Stage} medianMs={stage.MedianMs:F3}");
        if (Environment.GetEnvironmentVariable("HAPPY_PHOTON_STAGE_REPORT_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,
                $"FullSizeExport-{caseName}{(smoke ? "-smoke" : "")}.json"),
                JsonSerializer.Serialize(report, CullPerfFiles.Json) + Environment.NewLine);
        }
    }

    private static void ConfigureCase(ExportSettings settings, bool enabled)
    {
        if (enabled) settings.Watermark.Restore(new WatermarkSpec("© Jane Doe 2026",
            FontManager.Current.DefaultFontFamily.Name), enabled: true);
    }

    private static void AssertChangedPixels(IBaseImageLoader loader, ImageFile file, ExportSettings settings)
    {
        using var source = loader.LoadFullBase(file, BaseDecodeSettings.From(file.EditSettings), CancellationToken.None);
        Assert.NotNull(source);
        using var upstream = new RenderPipeline().RenderDisplayRec2020(new RenderRequest(source,
            file.EditSettings, RenderIntent.Export, null, new RenderOptions(false, false)));
        using var off = RenderFinalizer.Finalize(upstream, null, settings.OutputColorSpace,
            settings.OutputSharpening, false);
        using var marked = RenderFinalizer.Finalize(upstream, null, settings.OutputColorSpace,
            settings.OutputSharpening, false, watermark: settings.Watermark.Snapshot());
        using var before = off.GetPixelsUnsafe();
        using var after = marked.GetPixelsUnsafe();
        // Compare the bottom strip outside the timed export, without allocating a full-frame copy.
        var plain = before.GetArea(0, 3600, 6000, 400)!;
        var ink = after.GetArea(0, 3600, 6000, 400)!;
        Assert.True(plain.Zip(ink).Count(pair => pair.First != pair.Second) > 0);
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.Order().ToArray();
        return ordered[ordered.Length / 2];
    }

    private sealed record Tick(double TotalMs, Dictionary<string, double> Stages);
}
