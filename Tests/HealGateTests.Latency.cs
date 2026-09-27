using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class HealGateTests
{
    [Fact]
    public void G3Tick()
    {
        OptIn();
        using var pair = Loader().LoadPreviewBaseWithOutcome(LocalFile(), BaseDecodeSettings.Default, CancellationToken.None).Pair;
        Assert.NotNull(pair);
        var basis = pair.Interactive; var settings = HealWorkloads.LH8(); var pipeline = new RenderPipeline();
        var repairedSettings = settings.Clone(); repairedSettings.Repairs = HealWorkloads.Repairs(HealWorkloads.S64());
        void Tick(bool heal)
        {
            using var render = pipeline.Render(new(basis, heal ? repairedSettings : settings, RenderIntent.Preview, 1600, new(false, false)));
        }
        for (var warm = 0; warm < 10; warm++) { Tick(false); Tick(true); }
        var off = new double[5]; var on = new double[5]; var copy = new double[5];
        for (var i = 0; i < 5; i++)
        {
            if (i % 2 == 0) { off[i] = Time(() => Tick(false)); on[i] = Time(() => Tick(true)); }
            else { on[i] = Time(() => Tick(true)); off[i] = Time(() => Tick(false)); }
            copy[i] = Time(() =>
            {
                using var pixels = new MagickImage(basis.Pixels);
                // A clone is lazy; the first write pays the full native cache detachment.
                using var area = pixels.GetPixels();
                var first = area.GetArea(0, 0, 1, 1)!;
                pixels.ImportPixels(first, new PixelImportSettings(0, 0, 1, 1, StorageType.Quantum, PixelMapping.RGB));
            });
        }
        Report("G3", new { size = Size(basis), off, on, copy, control = Median(off), tick = Median(on),
            increment = Median(on.Zip(off, (a, b) => a - b)), copyMs = Median(copy),
            controlMin = basis.Info.IsRawSource ? 38 : 34, controlMax = basis.Info.IsRawSource ? 60 : 53,
            tickLimit = 150, incrementLimit = 40, samples = 5, warmupPairs = 10 });
        // Fresh-process aggregation in RunHealGates.ps1 owns the five-process median gates.
    }

    [Fact]
    public async Task G4Contention()
    {
        OptIn();
        using var pair = Loader().LoadPreviewBaseWithOutcome(LocalFile(), BaseDecodeSettings.Default, CancellationToken.None).Pair;
        Assert.NotNull(pair); Assert.NotNull(pair.Large);
        var small = pair.Interactive; var large = pair.Large;
        var restingSettings = RenderSequenceGoldenTests.Settings();
        var settings = restingSettings.Clone(); settings.Locals = HealWorkloads.LH8().Locals;
        var pipeline = new RenderPipeline();
        var limitText = Environment.GetEnvironmentVariable("HAPPY_PHOTON_HEAL_AREA_LIMIT");
        double? areaLimit = limitText == null ? Repair.MaximumArea : double.Parse(limitText, System.Globalization.CultureInfo.InvariantCulture);
        if (areaLimit != null) Assert.True(areaLimit >= HealWorkloads.Area(HealWorkloads.S64()));
        var area64 = Environment.GetEnvironmentVariable("HAPPY_PHOTON_HEAL_WORKLOAD") == "SArea64";
        var spots = area64 ? HealWorkloads.SArea64((int)small.Pixels.Width, (int)small.Pixels.Height)
            : HealWorkloads.SCap((int)small.Pixels.Width, (int)small.Pixels.Height, areaLimit);
        var repairedSettings = settings.Clone(); repairedSettings.Repairs = HealWorkloads.Repairs(spots);
        void Tick(bool heal)
        {
            using var rendered = pipeline.Render(new(small, heal ? repairedSettings : settings, RenderIntent.Preview, 1600, new(false, false)));
        }
        for (var warm = 0; warm < 3; warm++) { Tick(false); Tick(true); }
        var off = new double[5]; var on = new double[5];
        for (var sample = 0; sample < 5; sample++) for (var step = 0; step < 2; step++)
        {
            var heal = (sample + step) % 2 == 1;
            using var started = new ManualResetEventSlim();
            var execution = RenderExecutionOptions.Resting(CancellationToken.None, 2,
                stage => { if (stage == (large.Info.IsRawSource ? "raw-crossing" : "standard-tone")) started.Set(); });
            var resting = Task.Run(() => pipeline.RenderResting(new(large, restingSettings,
                RenderIntent.Preview, 3200, new(false, false)), execution));
            try
            {
                Assert.True(started.Wait(TestWaits.Condition)); Assert.False(resting.IsCompleted);
                (heal ? on : off)[sample] = Time(() => Tick(heal));
            }
            finally { using var result = await resting; }
        }
        Report("G4", new { size = Size(small), resting = Size(large), areaLimit, spots = spots.Length, topology = area64 ? "SArea64-serpentine-8x8" : "SCap6-serpentine-11x6-or-6x11",
            totalArea = HealWorkloads.Area(spots), minimumArea = HealWorkloads.Area(HealWorkloads.S64()),
            off, on, control = Median(off), tick = Median(on), controlMin = 95, controlMax = 145, tickLimit = 175 });
    }
}
