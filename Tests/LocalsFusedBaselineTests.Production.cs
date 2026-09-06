using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsFusedBaselineTests
{
    private static EditSettings LocalSettings(BaseImage b, double feather, double coverage)
    {
        var w = (int)b.Pixels.Width;
        var h = (int)b.Pixels.Height;
        var mask = Mask.Create(w, h, feather, coverage);
        return new EditSettings { Locals = [new() { Cu = mask.Cx * Math.Max(w, h) / w,
            Cv = mask.Cy * Math.Max(w, h) / h, Feather = feather, Angle = 30, Exposure = 2 }] };
    }

    [Fact]
    public void QualifiedG1G7() => TickAndMemory(false);

    [Fact]
    public void QualifiedRadialG1G7() => TickAndMemory(true);

    [Fact]
    public void QualifiedRadialG2() => TickAndMemory(true, true);

    private void TickAndMemory(bool radial, bool eight = false)
    {
        OptIn(); using var b = Load(false);
        var settings = radial ? RadialSettings(b, eight) : LocalSettings(b, .25, .45);
        var coverage = radial ? _radialCoverage : .45;
        var off = new double[Samples]; var on = new double[Samples];
        var memory = new double[Samples]; var allocation = new double[Samples];
        void Tick(EditSettings s) { using var result = new RenderPipeline().Render(
            new(b, s, RenderIntent.Preview, 1600, new(false, false))); }
        Tick(Settings); Tick(settings);
        for (var i = 0; i < Samples; i++)
        {
            var a = Measure(() => Tick(Settings)); var z = Measure(() => Tick(settings));
            off[i] = a.Ms; on[i] = z.Ms;
            memory[i] = z.Peak - a.Peak; allocation[i] = z.Allocated - a.Allocated;
        }
        var delta = Median(on.Zip(off, (z, a) => z - a).ToArray());
        Print(coverage, $"production_tick_off={Median(off):F4} on={Median(on):F4} paired_delta={delta:F4} " +
            $"private_delta={Median(memory)} caller_allocation_delta={Median(allocation)}");
        output.WriteLine($"tick_off_samples=[{string.Join(',', off)}] tick_on_samples=[{string.Join(',', on)}]");
        // G1 bounds the complete tick and the adjustment-stage delta separately.
        void Stage(EditSettings s) { using var result = ProductionGeometry(b, s, false); }
        Stage(Settings); Stage(settings);
        var stageOff = new double[Samples]; var stageOn = new double[Samples];
        for (var i = 0; i < Samples; i++)
        {
            stageOff[i] = Measure(() => Stage(Settings)).Ms;
            stageOn[i] = Measure(() => Stage(settings)).Ms;
        }
        var stageDelta = Median(stageOn.Zip(stageOff, (z, a) => z - a).ToArray());
        Print(coverage, $"production_stage_off={Median(stageOff):F4} on={Median(stageOn):F4} paired_delta={stageDelta:F4}");
        Assert.True(Median(on) <= 150, "G1 tick");
        Assert.True(stageDelta <= 45, "G1 stage delta");
        Assert.True(Median(memory) <= b.Pixels.Width * b.Pixels.Height * 6, "G7 private memory");
        Assert.True(Median(allocation) <= 65536, "G7 allocation");
        // R8 is frozen geometry; its union coverage depends on the fixture aspect (run 245:
        // RAW 3:2 44.45 %, HEIC portrait 4:3 34.32 %), so the window records rather than sizes it.
        if (eight) Assert.InRange(_radialCoverage, .30, .50);
    }

    [Fact]
    public void QualifiedG2()
    {
        OptIn(); using var b = Load(false);
        var deltas = new List<double>();
        foreach (var coverage in new[] { .2, .45, 1d })
        {
            var settings = LocalSettings(b, .25, coverage);
            void Run(EditSettings s) { using var result = ProductionGeometry(b, s); }
            Run(Settings); Run(settings);
            const int coverageSamples = 15;
            var a = new double[coverageSamples]; var z = new double[coverageSamples];
            for (var i = 0; i < coverageSamples; i++) { a[i] = Time(() => Run(Settings)); z[i] = Time(() => Run(settings)); }
            var delta = Median(z) - Median(a); deltas.Add(delta);
            Print(coverage, $"production_stage_off={Median(a):F4} on={Median(z):F4} delta={delta:F4}");
            Assert.True(Median(z) <= 150);
        }
        Assert.True(deltas[0] <= deltas[1] && deltas[1] <= deltas[2], "G2 monotone coverage");
    }

    [Fact]
    public void QualifiedG5()
    {
        OptIn();
        using var full = Fixture.StartsWith("synthetic") ? Synthetic() : Load(true);
        using var preview = Fixture.StartsWith("synthetic") ? SyntheticPreview(full) : Load(false);
        foreach (var feather in new[] { 0d, -1d, .25, .001 })
        {
            var settings = feather > 0 ? LocalSettings(preview, feather, .45) : new EditSettings { Exposure = feather < 0 ? 2 : 0 };
            using var p = ProductionGeometry(preview, settings);
            using var e = ProductionGeometry(full, settings);
            WysiwygTests.AlignForComparison(e, p);
            var comparison = GoldenImageComparer.Compare(e, p, GoldenComparisonDomain.DisplaySrgb);
            Print(feather > 0 ? .45 : 0, $"production_G5 feather={feather} mean={comparison.MeanDeltaE:F6} p99={comparison.P99DeltaE:F6}");
            if (feather <= 0 || Fixture.StartsWith("synthetic")) continue;
            var (mean, p99) = Fixture.EndsWith("cr2") ? (feather == .25 ? (3d, 15d) : (3.6, 18d)) :
                (feather == .25 ? (1.3, 14.5) : (1.6, 18.5));
            Assert.True(comparison.MeanDeltaE <= mean && comparison.P99DeltaE <= p99, "G5 frozen absolute bound");
        }
    }

    private static MagickImage ProductionGeometry(BaseImage b, EditSettings settings, bool finalize = true)
    {
        using var geometry = RenderGeometry.Apply(b.Pixels, settings, out var trace);
        var locals = RenderLocals.Create(settings, trace, (int)geometry.Width, (int)geometry.Height);
        var wb = RenderChromaticStage.CreateWhiteBalanceMatrix(b.Info, settings);
        if (b.Info.IsRawSource)
            new AgxCrossing(new(settings.Exposure, b.Info.SourceExposureBiasEv, 0, 0, 0, settings.Curve), wb,
                locals: locals).Apply(geometry);
        else
        {
            var matrix = ChromaticAdaptation.NormalizeForRender(wb);
            var tone = new ToneParams(settings.Exposure + b.Info.SourceExposureBiasEv, matrix.Fold,
                0, 0, 0, 0, false, settings.Curve);
            var luts = ToneLut.ComposeCached(tone);
            if (locals == null) ToneLutApplicator.Apply(geometry, matrix.Matrix, luts);
            else ToneLutApplicator.ApplyLocals(geometry, matrix.Matrix, luts, tone, locals, null);
        }
        if (!finalize) return new MagickImage(geometry);
        RenderColorEncoding.RetagAsSrgb(geometry);
        return RenderFinalizer.Finalize(geometry, null, OutputColorSpace.Srgb, OutputSharpeningMode.Off, false, effects: null);
    }

    [Fact]
    public void QualifiedG6()
    {
        OptIn(); using var b = Load(false);
        var settings = LocalSettings(b, .25, 1);
        settings.Locals![0].Exposure = 1; settings.Exposure = -1;
        using var baseline = ProductionGeometry(b, Settings);
        using var recovered = ProductionGeometry(b, settings);
        var max = RenderPipelineTestSupport.ReadPixels(baseline).Zip(RenderPipelineTestSupport.ReadPixels(recovered),
            (a, z) => Math.Abs(a - z)).Max();
        using var stageOff = ProductionGeometry(b, Settings, false);
        using var stageOn = ProductionGeometry(b, settings, false);
        var stageMax = RenderPipelineTestSupport.ReadPixels(stageOff).Zip(RenderPipelineTestSupport.ReadPixels(stageOn),
            (a, z) => Math.Abs(a - z)).Max();
        // Baseline Kernel is hard-pinned +2 EV; use -2 global to measure its own recovery.
        var kernel = new Kernel(b.Info, new EditSettings { Exposure = -2 });
        var reference = RenderPipelineTestSupport.ReadPixels(b.Pixels);
        kernel.Apply(reference, Mask.Create((int)b.Pixels.Width, (int)b.Pixels.Height, .25, 1), true);
        var prototypeMax = RenderPipelineTestSupport.ReadPixels(stageOff).Zip(reference, (a, z) => Math.Abs(a - z)).Max();
        var referenceDifference = RenderPipelineTestSupport.ReadPixels(stageOn).Zip(reference,
            (a, z) => Math.Abs(a - z)).Max();
        Print(1, $"G6 max_code={max} crossing_max={stageMax} prototype_recovery_vs_LUT={prototypeMax} " +
            $"production_vs_prototype={referenceDifference}");
        Assert.Equal(0, referenceDifference);
        Assert.InRange(stageMax, 0, 1);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExactBypassAndPrototypeOracle(bool raw, bool mono)
    {
        var random = new Random(243);
        var values = Enumerable.Range(0, 12000).Select(_ => (ushort)random.Next(65536)).ToArray();
        if (mono) for (var i = 0; i < values.Length; i += 3) values[i + 1] = values[i + 2] = values[i];
        using var b = RenderPipelineTestSupport.CreateBase(values, raw, 40, isMonochrome: mono);
        using var baseline = ProductionGeometry(b, Settings);
        var expected = RenderPipelineTestSupport.ReadPixels(baseline);
        foreach (var variant in new[] { "disabled", "neutral", "cancel" })
        {
            var settings = LocalSettings(b, .25, 1);
            var local = settings.Locals![0];
            if (variant == "disabled") local.Enabled = false;
            if (variant == "neutral") local.Exposure = 0;
            if (variant == "cancel") { local.Exposure = 1; settings.Locals.Add(local with { Id = Guid.NewGuid().ToString("N"), Ordinal = 2, Exposure = -1 }); }
            using var result = ProductionGeometry(b, settings);
            Assert.Equal(expected, RenderPipelineTestSupport.ReadPixels(result));
            using var geometry = RenderGeometry.Apply(b.Pixels, settings, out var trace);
            if (variant != "cancel") Assert.Null(RenderLocals.Create(settings, trace, 100, 40));
        }
        var active = LocalSettings(b, .25, .45);
        using var actual = ProductionGeometry(b, active);
        using var prototype = Geometric(b, Mask.Create(100, 40, .25, .45), true, Settings);
        var actualValues = RenderPipelineTestSupport.ReadPixels(actual);
        Assert.InRange(actualValues.Zip(RenderPipelineTestSupport.ReadPixels(prototype), (a, z) => Math.Abs(a - z)).Max(), 0, 1);
        if (mono) for (var i = 0; i < actualValues.Length; i += 3)
        { Assert.Equal(actualValues[i], actualValues[i + 1]); Assert.Equal(actualValues[i], actualValues[i + 2]); }
        Assert.Equal(values, RenderPipelineTestSupport.ReadPixels(b.Pixels));
    }
}
