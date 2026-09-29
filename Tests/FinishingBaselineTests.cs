using System.Diagnostics;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class FinishingBaselineTests(ITestOutputHelper output)
{
    private static string Fixture => Environment.GetEnvironmentVariable("HAPPY_PHOTON_OPS_FIXTURE") == "standard"
        ? "iphone-14-pro-iso-1000.heic" : "canon-eos-6d-iso-6400.cr2";

    private static readonly string[] ReviewFixtures =
    [
        "canon-eos-350d.cr2", "canon-eos-6d-iso-6400.cr2", "nikon-d300-colorchecker.nef",
        "nikon-d70-burst-1.nef", "nikon-d70-burst-2.nef", "fujifilm-x30.raf",
        "pentax-k-r.dng", "iphone-14-pro-iso-1000.heic", "reference.heic",
        "srgb-reference.jpg", "adobe-rgb-reference.jpg"
    ];

    private static IBaseImageLoader Loader() => new GatedBaseImageLoader(
        new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()), new SourceAvailabilityService());

    private static ImageFile LocalFile(string name)
    {
        var path = GoldenTestPaths.Asset(name);
        Assert.Equal(SourceAvailability.AvailableLocally, new SourceAvailabilityService().GetAvailability(path));

        return new(path);
    }

    private static void OptIn()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_FINISHING_CONTROLS") != "1",
            "Opt-in controls only; no candidate render arms");
        Assert.Equal("1", Environment.GetEnvironmentVariable("HAPPY_PHOTON_FULL_CPU"));
        PerfEnvironment.AssertFullCpu();
#if DEBUG
        Assert.Fail("Use Release");
#endif
    }

    private void Report(string gate, string fixture, object values) =>
        output.WriteLine("FINISHING_CONTROL " + JsonSerializer.Serialize(new
        {
            gate, fixture, pid = Environment.ProcessId, cpu = Environment.ProcessorCount, values
        }));

    private static double Median(IEnumerable<double> values) => values.Order().ElementAt(values.Count() / 2);

    private static double Time(Action action)
    {
        var start = Stopwatch.GetTimestamp();
        action();

        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    [Fact]
    public void G1Tick()
    {
        OptIn();
        using var pair = Loader().LoadPreviewBaseWithOutcome(LocalFile(Fixture), BaseDecodeSettings.Default, default).Pair;
        Assert.NotNull(pair);

        void Tick()
        {
            using var result = new RenderPipeline().Render(
                new(pair.Interactive, new(), RenderIntent.Preview, 1600, new(false, false)));
        }

        // Preserve OPS-WP1's pair cadence; both slots are NL at this stop.
        for (var warm = 0; warm < 10; warm++)
        {
            Tick();
            Tick();
        }

        var off = new double[5];
        var repeat = new double[5];

        for (var sample = 0; sample < 5; sample++)
        {
            for (var step = 0; step < 2; step++)
            {
                ((sample + step) % 2 == 0 ? off : repeat)[sample] = Time(Tick);
            }
        }

        Report("G1", Fixture, new { control = Median(off), off, repeat, warmPairs = 10, pairs = 5 });
    }

    [WindowsFact]
    public async Task G2Export()
    {
        OptIn();
        Assert.NotEqual("1", Environment.GetEnvironmentVariable("HAPPY_PHOTON_TEST_SKIA_ONLY"));
        using var preview = Loader().LoadPreviewBase(LocalFile(Fixture), BaseDecodeSettings.Default, default);
        Assert.NotNull(preview);
        var raw = preview.Info.IsRawSource;
        using var directory = new TemporaryDirectory();
        var file = LocalFile(Fixture);
        var pipeline = new RenderPipeline();
        var renders = 0;
        var service = new ImageExportService(pipeline, Loader(), new ExportMetadataService(),
            new DcpProfileService(new SourceAvailabilityService()), request =>
            {
                renders++;

                return pipeline.RenderDisplayRec2020(request);
            });
        var export = new ExportSettings
        {
            OutputFolder = directory.Path, Format = ExportFormat.Jpeg, Quality = 85,
            OutputColorSpace = OutputColorSpace.Srgb, ExportWeb = raw, ExportSmall = raw,
            WebMaxSize = 2048, SmallMaxSize = 1024,
            OutputSharpening = raw ? OutputSharpeningMode.Screen : OutputSharpeningMode.Off
        };
        var off = new double[5];
        var repeat = new double[5];

        for (var sample = -1; sample < 5; sample++)
        {
            for (var step = 0; step < 2; step++)
            {
                renders = 0;
                file.EditSettings = new();
                export.NamingPattern = "{name}-finishing-control-" + Guid.NewGuid().ToString("N");
                using var memory = new BrushPrivateMemorySampler();
                var start = Stopwatch.GetTimestamp();
                var result = await service.ExportBatchAsync([file], export);
                var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                memory.Finish();
                Assert.True(result.ExportedCount == 1,
                    string.Join(';', result.FailedTargets.Select(target => target.FailureReason)));
                Assert.Equal(1, renders);

                if (sample >= 0)
                {
                    ((sample + step) % 2 == 0 ? off : repeat)[sample] = ms;
                }
            }
        }

        Report("G2", Fixture, new
        {
            control = Median(off), off, repeat, warmPairs = 1, pairs = 5,
            variants = raw ? 3 : 1, backend = "Windows/WIC"
        });
    }

    [Fact]
    public void G3Parity()
    {
        OptIn();
        var loader = Loader();
        var file = LocalFile(Fixture);
        using var pair = loader.LoadPreviewBaseWithOutcome(file, BaseDecodeSettings.Default, default).Pair;
        using var full = loader.LoadFullBase(file, BaseDecodeSettings.Default, default);
        Assert.NotNull(pair?.Large);
        Assert.NotNull(full);
        using var reference = new RenderPipeline().Render(new(full, new(), RenderIntent.Export, null, new(false, false)));

        foreach (var basis in new[] { pair.Interactive, pair.Large })
        {
            using var preview = new RenderPipeline().Render(new(basis, new(), RenderIntent.Preview, null, new(false, false)));
            using var aligned = new MagickImage(reference.Image);
            WysiwygTests.AlignForComparison(aligned, preview.Image);
            var metric = GoldenImageComparer.Compare(aligned, preview.Image, GoldenComparisonDomain.DisplaySrgb);
            Report("G3", Fixture, new
            {
                width = basis.Pixels.Width, height = basis.Pixels.Height,
                mean = metric.MeanDeltaE, p99 = metric.P99DeltaE
            });
        }
    }

    [Fact]
    public void G4Clipping()
    {
        OptIn();

        foreach (var fixture in ReviewFixtures)
        {
            using var pair = Loader().LoadPreviewBaseWithOutcome(LocalFile(fixture), BaseDecodeSettings.Default, default).Pair;
            Assert.NotNull(pair);
            using var result = new RenderPipeline().Render(
                new(pair.Interactive, new(), RenderIntent.Preview, 1600, new(false, false)));
            var pixels = RenderPipelineTestSupport.ReadPixels(result.Image);
            var black = 0;
            var white = 0;

            for (var index = 0; index < pixels.Length; index += 3)
            {
                if (pixels[index] == 0 || pixels[index + 1] == 0 || pixels[index + 2] == 0) black++;
                if (pixels[index] == ushort.MaxValue || pixels[index + 1] == ushort.MaxValue ||
                    pixels[index + 2] == ushort.MaxValue) white++;
            }

            var count = pixels.Length / 3;
            Report("G4", fixture, new
            {
                width = result.Image.Width, height = result.Image.Height, pixels = count,
                black, white, blackPercent = 100.0 * black / count, whitePercent = 100.0 * white / count,
                scale = ushort.MaxValue, intent = "Preview", maxDimension = 1600
            });
        }
    }
}

