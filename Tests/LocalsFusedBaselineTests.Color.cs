using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsFusedBaselineTests
{
    private static EditSettings Colorize(EditSettings settings)
    {
        foreach (var local in settings.Locals!)
        { local.Temperature = 50; local.Tint = -50; local.Saturation = 100; }
        return settings;
    }

    [Fact] public Task QualifiedColorG9() => ContendedTick(true, color: true);
    [Fact] public Task QualifiedColorG9Eight() => ContendedTick(true, true, true, true);
    [Fact] public Task QualifiedColorG8Export() => ExportDelta(true, true);
    [Fact] public void QualifiedColorG1G7() => ColorTick(false);
    [Fact] public void QualifiedColorG2() => ColorTick(true);

    private void ColorTick(bool eight)
    {
        OptIn(); using var basis = Load(false);
        var control = eight ? RadialSettings(basis, true) : LocalSettings(basis, .25, .45);
        var color = Colorize(control.Clone());
        var tick = new double[Samples]; var controlTick = new double[Samples];
        var stage = new double[Samples]; var controlStage = new double[Samples];
        var memory = new double[Samples]; var allocation = new double[Samples];
        void Tick(EditSettings s) { using var result = new RenderPipeline().Render(
            new(basis, s, RenderIntent.Preview, 1600, new(false, false))); }
        void Stage(EditSettings s) { using var result = ProductionGeometry(basis, s, false); }
        Tick(Settings); Tick(control); Tick(color); Stage(Settings); Stage(control); Stage(color);
        for (var i = 0; i < Samples; i++)
        {
            var off = Measure(() => Tick(Settings));
            controlTick[i] = Measure(() => Tick(control)).Ms;
            var on = Measure(() => Tick(color));
            tick[i] = on.Ms; memory[i] = on.Peak - off.Peak; allocation[i] = on.Allocated - off.Allocated;
            var offStage = Measure(() => Stage(Settings)).Ms;
            controlStage[i] = Measure(() => Stage(control)).Ms - offStage;
            stage[i] = Measure(() => Stage(color)).Ms - offStage;
        }
        Print(eight ? _radialCoverage : .45, $"color C{(eight ? 8 : 1)} tick={Median(tick):F4} control_tick={Median(controlTick):F4} " +
            $"stage_delta={Median(stage):F4} control_delta={Median(controlStage):F4} " +
            $"private_delta={Median(memory)} caller_allocation={Median(allocation)}");
        Assert.True(Median(tick) <= 150, "G1/G2 tick");
        if (!eight)
        {
            Assert.True(Median(stage) <= (basis.Info.IsRawSource ? 38.9 : 44.4), "G1 frozen color stage ceiling");
            Assert.True(Median(controlStage) <= (basis.Info.IsRawSource ? 23.9 : 29.4), "G1 frozen control regression ceiling");
        }
        Assert.True(Median(memory) <= basis.Pixels.Width * basis.Pixels.Height * 6, "G7 private memory");
        Assert.True(Median(allocation) <= 65536, "G7 caller allocation");
    }

    [Fact]
    public void QualifiedColorG4b()
    {
        OptIn(); using var basis = Load(false);
        var global = GlobalColorWhite(basis);
        var local = Colorize(LocalSettings(basis, .25, 1));
        local.Locals![0].Exposure = local.Locals[0].Saturation = 0;
        var pipeline = new RenderPipeline();
        using var globalResult = pipeline.Render(new(basis, global, RenderIntent.Preview, 1600, new(false, false)));
        using var localResult = pipeline.Render(new(basis, local, RenderIntent.Preview, 1600, new(false, false)));
        var a = globalResult.Image;
        var b = localResult.Image;
        var errors = RenderPipelineTestSupport.ReadPixels(a).Zip(RenderPipelineTestSupport.ReadPixels(b),
            (x, y) => Math.Abs(x - y)).ToArray();
        var differingPixels = errors.Chunk(3).Count(channels => channels.Any(error => error > 1));
        Print(1, $"G4b full-weight local vs global W final_preview_max_code={errors.Max()} pixels_over_one_code={differingPixels}");
        using var stageGlobal = ProductionGeometry(basis, global, false);
        using var stageLocal = ProductionGeometry(basis, local, false);
        var globalCodes = RenderPipelineTestSupport.ReadPixels(stageGlobal);
        var localCodes = RenderPipelineTestSupport.ReadPixels(stageLocal);
        var worst = Enumerable.Range(0, globalCodes.Length).MaxBy(i => Math.Abs(globalCodes[i] - localCodes[i]));
        var pixel = worst / 3;
        var source = RenderPipelineTestSupport.ReadPixels(basis.Pixels);
        Print(1, $"G4b crossing max_code={Math.Abs(globalCodes[worst] - localCodes[worst])} pixel={pixel} " +
            $"global=[{string.Join(',', globalCodes.Skip(pixel * 3).Take(3))}] local=[{string.Join(',', localCodes.Skip(pixel * 3).Take(3))}]");
        if (basis.Info.IsRawSource)
        {
            var crossing = new AgxCrossing(new(0, basis.Info.SourceExposureBiasEv, 0, 0, 0, global.Curve),
                RenderChromaticStage.CreateWhiteBalanceMatrix(basis.Info, global));
            var analytic = crossing.TransformAnalytic(new(source[pixel * 3] / 65535d,
                source[pixel * 3 + 1] / 65535d, source[pixel * 3 + 2] / 65535d));
            Print(1, $"G4b global analytic=[{Math.Round(analytic.Red * 65535)},{Math.Round(analytic.Green * 65535)},{Math.Round(analytic.Blue * 65535)}] Fold={crossing.Fold}");
        }
        Assert.InRange(Math.Abs(globalCodes[worst] - localCodes[worst]), 0, 1);
    }

    private static EditSettings GlobalColorWhite(BaseImage basis) => new()
    {
        Wb = new() { Mode = WbMode.Custom, Kelvin = 1e6 / (1e6 / basis.Info.AsShotKelvin - 50),
            Tint = basis.Info.AsShotTint - 50 }
    };

    [Fact]
    public void QualifiedColorG5()
    {
        OptIn(); using var full = Load(true); using var preview = Load(false);
        var color = Colorize(LocalSettings(preview, .25, .45));
        color.Locals![0].Exposure = 0;
        foreach (var (arm, settings) in new[] { ("off", Settings), ("W", GlobalColorWhite(preview)), ("C1", color) })
        {
            using var p = ProductionGeometry(preview, settings);
            using var e = ProductionGeometry(full, settings);
            WysiwygTests.AlignForComparison(e, p);
            var result = GoldenImageComparer.Compare(e, p, GoldenComparisonDomain.DisplaySrgb);
            Print(arm == "C1" ? .45 : 0, $"G5 {arm} mean={result.MeanDeltaE:F6} p99={result.P99DeltaE:F6}");
            if (arm != "C1") continue;
            var (mean, p99) = preview.Info.IsRawSource ? (4.0, 20.5) : (1.8, 23.5);
            Assert.True(result.MeanDeltaE <= mean && result.P99DeltaE <= p99, "G5 frozen color bound");
        }
    }
}
