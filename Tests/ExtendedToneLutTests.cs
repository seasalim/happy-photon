using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExtendedToneLutTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NodesInteriorsAndTailsMatchAnalyticIncludingCurves(bool raw)
    {
        foreach (var amount in new[] { -100, 0, 100 })
        foreach (var curves in new[] { false, true })
        {
            var master = new CurveData(); var channel = new CurveData();
            if (curves)
            {
                master.AddPointAndReturnIndex(.3, .8); master.AddPointAndReturnIndex(.7, .2);
                channel.AddPointAndReturnIndex(.15, .8); channel.AddPointAndReturnIndex(.85, .1);
                if (amount == 100)
                {
                    // Deliberately narrow peaks through both curve stages; sampling
                    // a few interior points cannot by itself detect every knot.
                    master.Points = Enumerable.Range(0, 256).Select(i => new CurvePoint(i / 255d, i % 2)).ToList();
                    channel.Points = Enumerable.Range(0, 256).Select(i => new CurvePoint(i / 255d, 1 - i % 2)).ToList();
                    master.BuildLookupTable(); channel.BuildLookupTable();
                }
            }
            var exposure = amount / 25d; const double fold = 3.7;
            var rp = new AgxToneParameters(exposure, .3, amount, amount, -amount, master, channel);
            var sp = new ToneParams(exposure, fold, amount, amount, -amount, amount, true, master, channel);
            var tables = raw ? ExtendedToneLut.ComposeRaw(rp, fold) : ExtendedToneLut.ComposeStandard(sp);
            double Oracle(double value, CurveData? curve) => raw ? AgxToneEngine.EvaluateToneExtendedUnchecked(
                value, rp, Math.Pow(2, exposure + .3), Math.Log2(fold), AgxToneEngine.Slope(amount),
                AgxToneEngine.ToePower(-amount), AgxToneEngine.ShoulderPower(amount), curve) : ToneLut.Evaluate(sp, value, curve);
            var worst = 0d;
            foreach (var (table, curve) in new[] { (tables.Red, (CurveData?)channel), (tables.Green, null) })
            {
                for (var i = 0; i < ExtendedToneLut.Intervals; i++)
                foreach (var fraction in new[] { 0d, .13, .37, .63, .91 })
                {
                    var lo = ExtendedToneLut.Node(i); var hi = ExtendedToneLut.Node(i + 1);
                    Check(lo + (hi - lo) * fraction);
                }
                foreach (var boundary in new[] { ExtendedToneLut.Node(0), ExtendedToneLut.Node(ExtendedToneLut.Intervals) })
                foreach (var value in new[] { Math.BitDecrement(boundary), boundary, Math.BitIncrement(boundary) }) Check(value);
                foreach (var value in new[] { 0d, double.Epsilon, 1e-20, 1e20, double.MaxValue }) Check(value);
                void Check(double value)
                {
                    var expected = Oracle(value, curve); var actual = table.Evaluate(value);
                    // RAW outset max row absolute sum < 1.5, sRGB max derivative 12.92.
                    // This also bounds colored outset cancellation near encoded black.
                    var error = Math.Abs(expected - actual) * 65535 * (raw ? 1.5 * 12.92 : 1);
                    worst = Math.Max(worst, error);
                    Assert.True(error <= 1, $"raw={raw}, amount={amount}, curves={curves}, input={value:R}, Q16 bound={error:R}");
                }
            }
            output.WriteLine($"raw={raw} amount={amount} curves={curves} worst Q16 bound={worst:F6}");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ZeroLogPreservesLegacyPixelsIncludingAdjustedLocals(bool raw)
    {
        foreach (var bright in new[] { false, true })
        foreach (var locals in new[] { "none", "scalar", "color", "range" })
        {
            var samples = Enumerable.Range(0, 1536).Select(i => (ushort)(bright ? 40000 + i * 7 : 100 + i * 3)).ToArray();
            using var basis = RenderPipelineTestSupport.CreateBase(samples, raw);
            var settings = new EditSettings { Detail = new() { CaptureSharpen = 0 }, Contrast = 37,
                Locals = locals == "none" ? null : [new() { Cu = 2, Angle = 0, Feather = .001, Exposure = .7,
                    Saturation = locals == "color" ? 25 : 0,
                    Luminance = locals == "range" ? new() { Enabled = true, Lower = .1, Upper = .9, Softness = .1 } : null }] };
            var active = settings.Clone();
            if (bright) active.Blacks = -100; else active.Whites = 100;
            AssertSame(settings, active);
            // A points-only local entirely outside the image must also be an exact bypass.
            active = settings.Clone();
            active.Locals = new((active.Locals ?? []).Append(new() { Cu = -2, Angle = 0, Feather = .001, Whites = 100, Blacks = -100,
                Luminance = new() { Enabled = true, Lower = .1, Upper = .9 } }).ToArray());
            AssertSame(settings, active);
            void AssertSame(EditSettings control, EditSettings treatment)
            {
                using var a = new RenderPipeline().RenderDisplayRec2020(new(basis, control, RenderIntent.Export, null, new(false, false)));
                using var b = new RenderPipeline().RenderDisplayRec2020(new(basis, treatment, RenderIntent.Export, null, new(false, false)));
                Assert.Equal(RenderPipelineTestSupport.ReadPixels(a), RenderPipelineTestSupport.ReadPixels(b));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActiveFusedPixelsMatchAnalyticAfterChannelCurvesAndRawOutset(bool raw)
    {
        var random = new Random(283);
        var input = Enumerable.Range(0, 60000).Select(_ => (ushort)random.Next(65536)).ToArray();
        using var basis = RenderPipelineTestSupport.CreateBase(input, raw);
        var master = new CurveData(); master.AddPointAndReturnIndex(.3, .5);
        var channel = new CurveData(); channel.AddPointAndReturnIndex(.6, .3);
        var settings = new EditSettings { Exposure = .7, Whites = 60, Blacks = -60,
            Locals = [new() { Angle = 0, Cu = 2, Feather = .001, Exposure = 1, Whites = 100, Blacks = -100 }] };
        using var geometry = RenderGeometry.Apply(basis.Pixels, settings, out var frame);
        var plan = RenderLocals.Create(settings, frame, input.Length / 3, 1, info: basis.Info)!;
        var rp = new AgxToneParameters(.7, 0, 30, -40, 20, master, channel);
        var sp = new ToneParams(.7, 1, 10, 30, 20, -40, false, master, channel);
        var actual = (ushort[])input.Clone();
        if (raw) new AgxCrossing(rp, locals: plan).Apply(actual);
        else
        {
            using var image = new MagickImage(basis.Pixels);
            ToneLutApplicator.ApplyLocals(image, ChromaticAdaptation.Identity(), ToneLut.Compose(sp), sp, plan, null);
            actual = RenderPipelineTestSupport.ReadPixels(image);
        }
        var global = new WhitesBlacksOperator(60, -60, raw); var local = new WhitesBlacksOperator(100, -100, raw);
        var inset = new AgxCrossing.Matrix3x3(AgxToneEngine.InsetMatrix);
        var outset = new AgxCrossing.Matrix3x3(AgxToneEngine.OutsetMatrix);
        for (var i = 0; i < input.Length; i += 3)
        {
            double r = input[i] / 65535d, g = input[i + 1] / 65535d, b = input[i + 2] / 65535d;
            var position = WhitesBlacksOperator.Position((Rec2020Luminance.Red * r + Rec2020Luminance.Green * g +
                Rec2020Luminance.Blue * b) * Math.Pow(2, .7));
            var gain = 2 * double.Exp2(global.LogGain(position) + local.LogGain(position));
            r *= gain; g *= gain; b *= gain;
            if (raw) (r, g, b) = (inset.Row0(r, g, b), inset.Row1(r, g, b), inset.Row2(r, g, b));
            double Tone(double value, CurveData? curve) => raw ? AgxToneEngine.EvaluateToneExtendedUnchecked(value,
                rp, Math.Pow(2, .7), 0, AgxToneEngine.Slope(30), AgxToneEngine.ToePower(20),
                AgxToneEngine.ShoulderPower(-40), curve) : ToneLut.Evaluate(sp, value, curve);
            (r, g, b) = (Tone(r, channel), Tone(g, null), Tone(b, null));
            if (raw) (r, g, b) = (ToneLut.SrgbEncode(Math.Clamp(outset.Row0(r, g, b), 0, 1)),
                ToneLut.SrgbEncode(Math.Clamp(outset.Row1(r, g, b), 0, 1)),
                ToneLut.SrgbEncode(Math.Clamp(outset.Row2(r, g, b), 0, 1)));
            var expected = new[] { r, g, b };
            for (var c = 0; c < 3; c++)
                Assert.InRange(Math.Abs(actual[i + c] - Math.Round(expected[c] * 65535, MidpointRounding.AwayFromZero)), 0, 1);
        }
    }

    [Fact]
    public void CancellingLogGainsDoNotMarkThePixelAdjusted()
    {
        using var basis = RenderPipelineTestSupport.CreateBase([20000, 20000, 20000]);
        var settings = new EditSettings { Whites = 60, Blacks = -60,
            Locals = [new() { Cu = 2, Angle = 0, Feather = .001, Whites = -60, Blacks = 60 }] };
        using var geometry = RenderGeometry.Apply(basis.Pixels, settings, out var frame);
        var plan = RenderLocals.Create(settings, frame, 1, 1)!;
        foreach (var sample in new[] { .01, .18, .7 })
        {
            double r = sample, g = sample, b = sample;
            Assert.False(plan.ApplyColor(0, ref r, ref g, ref b, 1, out var points));
            Assert.False(points); Assert.Equal(sample, r); Assert.Equal(sample, g); Assert.Equal(sample, b);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CacheReusesToneIdentityAndSnapshotsCurves(bool raw)
    {
        var curve = new CurveData(); curve.AddPointAndReturnIndex(.4, .6);
        var rp = new AgxToneParameters(0, 0, 0, 0, 0, curve);
        var sp = new ToneParams(0, 1, 0, 0, 0, 0, false, curve);
        var key = raw ? AgxToneLut.ComposeCached(rp, 1) : ToneLut.ComposeCached(sp);
        var table = raw ? ExtendedToneLut.ForRaw(key, rp, 1) : ExtendedToneLut.ForStandard(key, sp);
        Assert.Same(table, raw ? ExtendedToneLut.ForRaw(key, rp, 1) : ExtendedToneLut.ForStandard(key, sp));
        Assert.Same(table.Red, table.Green); Assert.Same(table.Green, table.Blue);
        var before = table.Red.Evaluate(1e20);
        curve.MovePoint(2, 1, .1);
        Assert.Equal(before, table.Red.Evaluate(1e20));
        var nextKey = raw ? AgxToneLut.ComposeCached(rp, 1) : ToneLut.ComposeCached(sp);
        Assert.NotSame(table, raw ? ExtendedToneLut.ForRaw(nextKey, rp, 1) : ExtendedToneLut.ForStandard(nextKey, sp));
    }
}
