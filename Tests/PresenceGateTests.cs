using System.Diagnostics;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed partial class PresenceGateTests(ITestOutputHelper output)
{
    private static string Fixture => Environment.GetEnvironmentVariable("HAPPY_PHOTON_OPS_FIXTURE") == "standard"
        ? "iphone-14-pro-iso-1000.heic" : "canon-eos-6d-iso-6400.cr2";

    private static string Folder => Directory.CreateDirectory(
        Path.Combine(GoldenTestPaths.RepositoryRoot, "artifacts", "ops-wp3")).FullName;

    private static OpsArm[] Arms => [new("TX+", 60), new("TX-", -60),
        new("CL+", Clarity: 60), new("CL-", Clarity: -60), new("both", 40, 40)];

    private static EditSettings Settings(OpsArm arm) => new()
    {
        Texture = (int)arm.Texture,
        Clarity = (int)arm.Clarity
    };

    private static IBaseImageLoader Loader() => new GatedBaseImageLoader(
        new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()), new SourceAvailabilityService());

    private static ImageFile LocalFile()
    {
        var path = GoldenTestPaths.Asset(Fixture);
        Assert.Equal(SourceAvailability.AvailableLocally, new SourceAvailabilityService().GetAvailability(path));

        return new(path);
    }

    private static void OptIn()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1", "Opt-in OPS-WP3 qualification");
        PerfEnvironment.AssertFullCpu();
#if DEBUG
        Assert.Fail("Use Release");
#endif
    }

    private void Report(string gate, object values) => output.WriteLine("OPS_WP3 " + JsonSerializer.Serialize(new
    {
        gate, fixture = Fixture, pid = Environment.ProcessId, values
    }));

    private static double Time(Action action)
    {
        var start = Stopwatch.GetTimestamp();
        action();

        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();

        return sorted[sorted.Length / 2];
    }

    private static MagickImage Render(BaseImage basis, EditSettings settings, RenderIntent intent) =>
        new CurrentPipelineGoldenRenderer().Render(basis, settings, intent,
            (int)Math.Max(basis.Pixels.Width, basis.Pixels.Height));

    [Fact]
    public void G1Ticks()
    {
        OptIn();
        using var pair = Loader().LoadPreviewBaseWithOutcome(LocalFile(), BaseDecodeSettings.Default, default).Pair;
        Assert.NotNull(pair);

        foreach (var arm in Arms)
        {
            void Tick(bool active)
            {
                using var result = new RenderPipeline().Render(new(pair.Interactive,
                    active ? Settings(arm) : new(), RenderIntent.Preview, 1600, new(false, false)));
            }

            for (var warm = 0; warm < 10; warm++)
            {
                Tick(false);
                Tick(true);
            }

            var off = new double[5];
            var on = new double[5];

            for (var sample = 0; sample < 5; sample++)
            for (var step = 0; step < 2; step++)
            {
                var active = (sample + step) % 2 == 1;
                (active ? on : off)[sample] = Time(() => Tick(active));
            }

            Report("G1", new { arm = arm.Name, control = Median(off), active = Median(on),
                increment = Median(on.Zip(off, (a, b) => a - b)), off, on });
        }

        // RunPresenceGates.ps1 asserts medians from five independent fresh processes.
    }

    [Fact]
    public async Task G2Contention()
    {
        OptIn();
        // Match BrushContendedTick's NL control: full decode -> 3200 -> 1600.
        using var full = Loader().LoadFullBase(LocalFile(), BaseDecodeSettings.Default, default);
        Assert.NotNull(full);
        var largePixels = new MagickImage(full.Pixels);
        BitmapConversionService.ResizeToMaxDimension(largePixels, 3200);
        using var large = new BaseImage(largePixels, full.Info);
        var smallPixels = new MagickImage(large.Pixels);
        BitmapConversionService.ResizeToMaxDimension(smallPixels, 1600);
        using var small = new BaseImage(smallPixels, full.Info);
        var restingSettings = RenderSequenceGoldenTests.Settings();
        var activeSettings = restingSettings.Clone();
        activeSettings.Texture = activeSettings.Clarity = 40;
        var pipeline = new RenderPipeline();

        void Tick(bool active)
        {
            using var result = pipeline.Render(new(small, active ? activeSettings : restingSettings,
                RenderIntent.Preview, 1600, new(false, false)));
        }

        for (var warm = 0; warm < 3; warm++)
        {
            Tick(false);
            Tick(true);
        }

        var off = new double[5];
        var on = new double[5];

        for (var sample = 0; sample < 5; sample++)
        for (var step = 0; step < 2; step++)
        {
            var active = (sample + step) % 2 == 1;
            using var started = new ManualResetEventSlim();
            var execution = RenderExecutionOptions.Resting(default, 2, stage =>
            {
                if (stage == (large.Info.IsRawSource ? "raw-crossing" : "standard-tone")) started.Set();
            });
            var resting = Task.Run(() => pipeline.RenderResting(new(large, restingSettings,
                RenderIntent.Preview, 3200, new(false, false)), execution));

            try
            {
                Assert.True(started.Wait(TestWaits.Condition));
                Assert.False(resting.IsCompleted);
                (active ? on : off)[sample] = Time(() => Tick(active));
            }
            finally
            {
                using var result = await resting;
            }
        }

        Report("G2", new { arm = "both", control = Median(off), active = Median(on), off, on,
            width = small.Pixels.Width, height = small.Pixels.Height,
            restingWidth = large.Pixels.Width, restingHeight = large.Pixels.Height,
            restingCap = 2, allOverlappedAtStart = true });
    }

    [Fact]
    public void G5Parity()
    {
        OptIn();
        var loader = Loader();
        var file = LocalFile();
        using var pair = loader.LoadPreviewBaseWithOutcome(file, BaseDecodeSettings.Default, default).Pair;
        using var full = loader.LoadFullBase(file, BaseDecodeSettings.Default, default);
        Assert.NotNull(pair?.Large);
        Assert.NotNull(full);
        using var controlFull = Render(full, new(), RenderIntent.Export);
        var pass = true;

        foreach (var basis in new[] { pair.Interactive, pair.Large })
        {
            using var control = Render(basis, new(), RenderIntent.Preview);
            var baseline = Compare(controlFull, control);
            var pinned = (basis.Info.IsRawSource, ReferenceEquals(basis, pair.Interactive)) switch
            {
                (true, true) => (1.675, 10.015),
                (true, false) => (3.754, 23.887),
                (false, true) => (.941, 10.634),
                _ => (.418, 5.618)
            };
            Assert.InRange(baseline.MeanDeltaE, pinned.Item1 * .85, pinned.Item1 * 1.15);
            Assert.InRange(baseline.P99DeltaE, pinned.Item2 * .85, pinned.Item2 * 1.15);

            foreach (var texture in new[] { true, false })
            foreach (var amount in new[] { -100, 100 })
            {
                var settings = new EditSettings { Texture = texture ? amount : 0, Clarity = texture ? 0 : amount };
                using var expected = Render(full, settings, RenderIntent.Export);
                using var actual = Render(basis, settings, RenderIntent.Preview);
                var metric = Compare(expected, actual);
                var ok = metric.MeanDeltaE <= baseline.MeanDeltaE + .5 && metric.P99DeltaE <= baseline.P99DeltaE + 2;
                var reportOnly = amount > 0;
                Report("G5", new { arm = texture ? "TX" : "CL", amount, reportOnly,
                    width = basis.Pixels.Width, height = basis.Pixels.Height,
                    controlMean = baseline.MeanDeltaE, controlP99 = baseline.P99DeltaE,
                    mean = metric.MeanDeltaE, p99 = metric.P99DeltaE, pass = ok });

                if (!reportOnly) pass &= ok;
            }
        }

        Assert.True(pass, "G5 negative-arm relative parity miss; D-6 exempts positive arms until WP9 merges.");
    }

    private static GoldenComparison Compare(MagickImage full, MagickImage preview)
    {
        using var aligned = new MagickImage(full);
        WysiwygTests.AlignForComparison(aligned, preview);

        return GoldenImageComparer.Compare(aligned, preview, GoldenComparisonDomain.DisplaySrgb);
    }
}
