using System.Reflection;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class WhitesBlacksGateTests
{
    [Fact]
    public void R1CostDiagnostic()
    {
        OptIn(); using var pair = Loader().LoadPreviewBaseWithOutcome(File(), BaseDecodeSettings.Default, CancellationToken.None).Pair;
        Assert.NotNull(pair); var basis = pair.Interactive;
        using var geometry = RenderGeometry.Apply(basis.Pixels, new(), out var frame);
        var width = (int)geometry.Width; var height = (int)geometry.Height;
        RenderLocals Plan(EditSettings settings) => RenderLocals.Create(settings, frame, width, height, info: basis.Info)!;
        // Test-only identity color plans force basis capture; no production diagnostic switches.
        // Tiny nonzero exposure forces the analytic path with essentially unchanged input.
        const double ev = 1e-9;
        var scalar = Plan(new() { Locals = [new() { Cu = 2, Feather = .001, Exposure = ev }] });
        RenderLocals BasisPlan(double exposure)
        {
            var plan = Plan(new() { Locals = [new() { Cu = 2, Feather = .001, Saturation = 1 }] });
            var gain = Math.Pow(2, exposure);
            var color = new AgxCrossing.Matrix3x3(gain, 0, 0, 0, gain, 0, 0, 0, gain);
            typeof(RenderLocals).GetField("_colors", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(plan, new AgxCrossing.Matrix3x3?[] { color });
            return plan;
        }
        var plans = new RenderLocals?[] { null, scalar, BasisPlan(0), BasisPlan(ev), Plan(Points(60)), Plan(Points(-60)) };
        string[] names = ["lut", "analytic-scalar", "basis-lut", "basis-analytic", "points+60", "points-60"];
        var wb = RenderChromaticStage.CreateWhiteBalanceMatrix(basis.Info, new());
        var normalized = RenderChromaticStage.CreateNormalizedMatrix(basis.Info, new());
        var parameters = new ToneParams(basis.Info.SourceExposureBiasEv, normalized.Fold, 0, 0, 0, 0, false, new());
        var luts = ToneLut.ComposeCached(parameters);
        var crossings = plans.Select(plan => new AgxCrossing(new(0, basis.Info.SourceExposureBiasEv, 0, 0, 0, new()),
            wb, locals: plan)).ToArray();
        var samples = plans.Select(_ => new List<double>()).ToArray();
        for (var round = -5; round < 9; round++)
        foreach (var arm in round % 2 == 0 ? Enumerable.Range(0, plans.Length) : Enumerable.Range(0, plans.Length).Reverse())
        {
            using var pixels = new MagickImage(geometry); // clone excluded from timed fused kernel
            var elapsed = Time(() =>
            {
                if (basis.Info.IsRawSource) crossings[arm].Apply(pixels);
                else if (plans[arm] is { } plan) ToneLutApplicator.ApplyLocals(pixels, normalized.Matrix, luts, parameters, plan, null);
                else ToneLutApplicator.Apply(pixels, normalized.Matrix, luts);
            });
            if (round >= 0) samples[arm].Add(elapsed);
        }
        var medians = samples.Select(Median).ToArray();
        Report("R1-cost-attribution", new { diagnostic = true, width, height, names, medians,
            analyticToneIncrement = medians[1] - medians[0], basisLutIncrement = medians[2] - medians[0],
            basisAnalyticIncrement = medians[3] - medians[1],
            note = "Fused-kernel probes, not additive G1 budgets; basis probes include one neutral local/mask. Setup and image cloning excluded." });
        OperatorMath(geometry, basis.Info);
        ExtendedToneBuild(basis.Info);
    }

    private void OperatorMath(MagickImage image, BaseImageInfo info)
    {
        var values = RenderPipelineTestSupport.ReadPixels(image);
        var count = values.Length / 3; var workers = Math.Min(Environment.ProcessorCount, Math.Max(1, (count + 32767) / 32768));
        var sums = new double[workers]; var exposure = Math.Pow(2, info.SourceExposureBiasEv);
        var global = new WhitesBlacksOperator(60, 60, info.IsRawSource);
        var local = new WhitesBlacksOperator(30, -20, info.IsRawSource);
        // Fixed fractional weights exercise all eight lookups; this isolates arithmetic,
        // not LH8's masks, ranges, or brush evaluation (the existing G2 measures those).
        foreach (var locals in new[] { 0, 8 })
        {
            var off = new List<double>(); var on = new List<double>();
            void Run(bool enabled) => Parallel.For(0, workers, worker =>
            {
                var sum = 0d;
                for (var p = count * worker / workers; p < count * (worker + 1) / workers; p++)
                {
                    double r = values[p * 3] / 65535d, g = values[p * 3 + 1] / 65535d, b = values[p * 3 + 2] / 65535d;
                    if (enabled)
                    {
                        var position = WhitesBlacksOperator.Position((Rec2020Luminance.Red * r + Rec2020Luminance.Green * g + Rec2020Luminance.Blue * b) * exposure);
                        var log = global.LogGain(position);
                        for (var i = 0; i < locals; i++) log += (i + 1) / 9d * local.LogGain(position);
                        var gain = double.Exp2(log); r *= gain; g *= gain; b *= gain;
                    }
                    sum += r + g + b;
                }
                sums[worker] = sum;
            });
            for (var round = -5; round < 9; round++)
            {
                double a, b;
                if (round % 2 == 0) { a = Time(() => Run(false)); b = Time(() => Run(true)); }
                else { b = Time(() => Run(true)); a = Time(() => Run(false)); }
                if (round >= 0) { off.Add(a); on.Add(b); }
            }
            Assert.True(double.IsFinite(sums.Sum()));
            Report("R1-operator-math", new { diagnostic = true, locals, control = Median(off), active = Median(on),
                increment = Median(on.Zip(off, (a, b) => a - b)), checksum = sums.Sum() });
        }
    }
}
