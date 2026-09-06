using System.Diagnostics;
using System.Reflection;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsFusedBaselineTests
{
    [Fact]
    public async Task QualifiedG8()
        => await ContendedTick(false);

    [Fact]
    public async Task QualifiedLinearContention() => await ContendedTick(true);

    [Fact]
    public async Task QualifiedRadialG9() => await ContendedTick(true, true);

    [Fact]
    public async Task QualifiedRadialG9Eight() => await ContendedTick(true, true, true);

    private async Task ContendedTick(bool realFixture, bool radial = false, bool eight = false, bool color = false)
    {
        OptIn();
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        using var loaded = realFixture ? Load(true) : null;
        var largePixels = realFixture ? new MagickImage(loaded!.Pixels) : null;
        if (largePixels != null) BitmapConversionService.ResizeToMaxDimension(largePixels, 3200);
        using var large = largePixels != null ? new BaseImage(largePixels, loaded!.Info) :
            (BaseImage)typeof(RenderSequenceGoldenTests)
                .GetMethod("CreateBase", flags)!.Invoke(null, ["raw", 3200, 2133])!;
        var settings = (EditSettings)typeof(RenderSequenceGoldenTests)
            .GetMethod("Settings", flags)!.Invoke(null, null)!;
        var pixels = new MagickImage(large.Pixels);
        BitmapConversionService.ResizeToMaxDimension(pixels, 1600);
        using var small = new BaseImage(pixels, large.Info);
        var localSettings = settings.Clone();
        localSettings.Locals = (radial ? RadialSettings(small, eight) : LocalSettings(small, .25, .45)).Locals;
        if (color) Colorize(localSettings);
        var interactive = new RenderRequest(small, localSettings, RenderIntent.Preview, 1600, new(false, false));
        var restingRequest = new RenderRequest(large, settings, RenderIntent.Preview, 3200, new(false, false));
        var pipeline = new RenderPipeline();
        for (var warm = 0; warm < 10; warm++) using (pipeline.Render(interactive)) { }
        var alone = new double[Samples];
        var concurrent = new double[Samples];
        for (var sample = 0; sample < Samples; sample++)
        {
            alone[sample] = Tick();
            using var started = new ManualResetEventSlim();
            var execution = RenderExecutionOptions.Resting(CancellationToken.None, 2,
                stage => { if (stage == (large.Info.IsRawSource ? "raw-crossing" : "standard-tone")) started.Set(); });
            var resting = Task.Run(() => pipeline.RenderResting(restingRequest, execution));
            try
            {
                Assert.True(started.Wait(TestWaits.Condition), "Resting never entered raw-crossing.");
                Assert.False(resting.IsCompleted, "The next tick must start during resting work.");
                concurrent[sample] = Tick();
            }
            finally { using var result = await resting; }
        }
        output.WriteLine($"contention fixture={(realFixture ? Fixture : "synthetic-RAW")} radial={radial} eight={eight} cpu={Environment.ProcessorCount} process={Environment.ProcessId} " +
            $"alone={Median(alone):F4} concurrent={Median(concurrent):F4} " +
            $"alone_samples=[{string.Join(',', alone)}] concurrent_samples=[{string.Join(',', concurrent)}] " +
            $"resting={large.Pixels.Width}x{large.Pixels.Height} cap=2 samples={Samples}");
        Assert.True(Median(concurrent) <= 150, "G8 contended tick");

        double Tick() => Time(() => { using var result = pipeline.Render(interactive); });
    }

    [Fact]
    public async Task QualifiedG9() => await ExportDelta(false);

    [Fact]
    public async Task QualifiedRadialG10() => await ExportDelta(true);

    private async Task ExportDelta(bool radial, bool color = false)
    {
        OptIn();
        using var preview = Load(false);
        using var directory = new TemporaryDirectory();
        var raw = Fixture.EndsWith("cr2");
        var settings = new ExportSettings { OutputFolder = directory.Path, Format = ExportFormat.Jpeg,
            Quality = 85, OutputColorSpace = OutputColorSpace.Srgb, ExportWeb = raw, ExportSmall = raw,
            WebMaxSize = 2048, SmallMaxSize = 1024,
            OutputSharpening = raw ? OutputSharpeningMode.Screen : OutputSharpeningMode.Off };
        var pipeline = new RenderPipeline();
        var renderCount = 0;
        var service = new ImageExportService(pipeline, Loader(), new ExportMetadataService(),
            new DcpProfileService(new SourceAvailabilityService()), request =>
            {
                Interlocked.Increment(ref renderCount);
                return pipeline.RenderDisplayRec2020(request);
            });
        var file = new ImageFile(GoldenTestPaths.Asset(Fixture));
        var active = radial ? RadialSettings(preview, color) : LocalSettings(preview, .25, .45);
        if (color) Colorize(active);
        var off = new double[Samples]; var on = new double[Samples];
        for (var sample = -1; sample < Samples; sample++)
        {
            var a = await Export(Settings, $"off-{sample + 1}");
            var z = await Export(active, $"on-{sample + 1}");
            if (sample >= 0) { off[sample] = a; on[sample] = z; }
        }
        var delta = Median(on.Zip(off, (z, a) => z - a).ToArray());
        // All variants share one full-resolution upstream render.
        var limit = Math.Max((raw ? 2911.2468 : 865.4567) * .05, 500);
        Print(.45, $"G9 export_off={Median(off):F4} on={Median(on):F4} paired_delta={delta:F4} " +
            $"limit={limit:F4} variants={(raw ? 3 : 1)}");
        Assert.True(delta <= limit, "G9 export delta");

        async Task<double> Export(EditSettings edits, string arm)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            file.EditSettings = edits;
            settings.NamingPattern = "{name}-" + arm;
            renderCount = 0;
            var start = Stopwatch.GetTimestamp();
            var result = await service.ExportBatchAsync([file], settings);
            Assert.True(result.ExportedCount == 1,
                string.Join(';', result.FailedTargets.Select(t => t.FailureReason)));
            Assert.Equal(1, renderCount);
            return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
    }
}
