using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class OpsKernelTests
{
    internal static ushort[] Sentinel(int width = 47, int height = 31)
    {
        var random = new Random(281);
        return Enumerable.Range(0, width * height * 3).Select(i => (ushort)(5000 + random.Next(50000))).ToArray();
    }

    [Theory]
    [InlineData(0, 1.0)] [InlineData(1, 1.0)] [InlineData(0, .291)] [InlineData(1, .291)]
    public void PresenceMatchesIndependentOracle(int clarity, double scale)
    {
        var source = Sentinel();
        using var basis = RenderPipelineTestSupport.CreateBase(source, height: 31);
        var info = basis.Info with { FullWidth = (int)Math.Round(47 / scale), FullHeight = (int)Math.Round(31 / scale) };
        foreach (var arm in new[] { new OpsArm("TX", 100), new OpsArm("TX", -100), new OpsArm("CL", Clarity: 100),
            new OpsArm("CL", Clarity: -100), new OpsArm("both", 40, 40) })
        {
            using var image = new MagickImage(basis.Pixels);
            OpsPresencePrototype.Apply(image, info, arm, (OpsClarity)clarity, workers: 2, bandRows: 1);
            var expected = OpsPresenceOracle.Apply(source, 47, 31, info.FullWidth, arm, (OpsClarity)clarity);
            MaxError(expected, RenderPipelineTestSupport.ReadPixels(image), 1);
        }
    }

    [Fact]
    public void PresenceZeroDoesNotAccessPixelsAndCancellationIsObserved()
    {
        using var basis = RenderPipelineTestSupport.CreateBase(Sentinel(), height: 31);
        var disposed = new MagickImage(basis.Pixels); disposed.Dispose();
        OpsPresencePrototype.Apply(disposed, basis.Info, OpsArm.Off, OpsClarity.Guided);
        Assert.Null(OpsAmountField.Create(100, null, 30, 50));
        Assert.Null(OpsAmountField.Create(100, HealWorkloads.LH8().Locals, 0, 0));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => OpsPresencePrototype.Apply(basis.Pixels, basis.Info,
            OpsArm.Stack, OpsClarity.Guided, cancellation: cancelled.Token));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void FusedOffEqualsProductionAndDehazeMatchesOracle(bool raw, bool refine)
    {
        var source = Sentinel();
        using var basis = RenderPipelineTestSupport.CreateBase(source, raw, 31);
        foreach (var settings in new[] { new EditSettings(), new EditSettings { Exposure = -2,
            Wb = new() { Mode = WbMode.Picked, Gains = [1.6, .9, 1.2] } } })
        {
            using var control = new RenderPipeline().RenderDisplayRec2020(new(basis, settings, RenderIntent.Export, null, new(false, false)));
            using var off = OpsRenderHarness.Upstream(basis, settings, OpsArm.Off, OpsClarity.Guided, refine, RenderIntent.Export, forceFused: true);
            MaxError(RenderPipelineTestSupport.ReadPixels(control), RenderPipelineTestSupport.ReadPixels(off), 0);
            var wb = RenderChromaticStage.CreateWhiteBalanceMatrix(basis.Info, settings);
            var prototype = OpsDehaze.Build(basis.Pixels, wb, 2);
            var oracle = OpsDehazeOracle.Build(source, 47, 31, wb);
            for (var c = 0; c < 3; c++) Assert.InRange(Math.Abs(prototype.Airlight[c] - oracle.Airlight[c]), 0, 1e-12);
            for (var i = 0; i < prototype.Transmission.Values.Length; i++)
                Assert.InRange(Math.Abs(prototype.Transmission.Values[i] - oracle.Transmission[i]), 0, 1e-12);
            foreach (var amount in new[] { -100, -50, 50, 100 })
            {
                using var actual = new MagickImage(basis.Pixels);
                OpsRenderHarness.Fused(actual, basis.Info, settings, new("DH", Dehaze: amount), refine, 2, 1);
                var expected = new ushort[source.Length]; var outside = 0;
                for (var i = 0; i < source.Length / 3; i++)
                {
                    var value = new double[3];
                    for (var c = 0; c < 3; c++) for (var j = 0; j < 3; j++) value[c] += wb[c, j] * source[i * 3 + j] / 65535d;
                    var corrected = oracle.Correct(value, (i % 47 + .5) / 47, (i / 47 + .5) / 31, amount, refine);
                    outside += corrected.Count(v => v < 0 || v > 1);
                    OpsDehazeOracle.Tone(corrected, basis.Info, settings).CopyTo(expected, i * 3);
                }
                if (amount == 100) Assert.True(outside > 0, "Oracle vectors must include signed/out-of-range corrections");
                MaxError(expected, RenderPipelineTestSupport.ReadPixels(actual), 1);
            }
        }
    }

    [Fact]
    public void AmountFieldsMatchProductionFinalRangeWeights()
    {
        const int w = 47, h = 31;
        using var basis = RenderPipelineTestSupport.CreateBase(Sentinel(), height: h);
        var settings = HealWorkloads.LH8(); var source = Sentinel();
        var field = OpsAmountField.Create(w * h, settings.Locals, 30, 50)!;
        using var geometry = RenderGeometry.Apply(basis.Pixels, settings, out var trace);
        var terms = settings.Locals!.Select(l => RenderLocals.Create(new EditSettings { Locals =
            [l with { Exposure = 1, Temperature = 0, Tint = 0, Saturation = 0 }] }, trace, w, h, info: basis.Info)!).ToArray();
        for (var i = 0; i < w * h; i++)
        {
            double r = source[i * 3] / 65535d, g = source[i * 3 + 1] / 65535d, b = source[i * 3 + 2] / 65535d, sum = 0;
            field.Fill(i, w, h, r, g, b, settings.Locals!);
            foreach (var term in terms)
            {
                var cr = r; var cg = g; var cb = b; term.ApplyColor(i, ref cr, ref cg, ref cb);
                sum += cr / r - 1; // +1 EV gives gain 1+weight, independently exposing the production final weight.
            }
            Assert.InRange(Math.Abs(field.Texture[i] - Math.Round(Math.Clamp(sum * 30, -200, 200) * 100)), 0, 1);
            Assert.InRange(Math.Abs(field.Clarity[i] - Math.Round(Math.Clamp(sum * 50, -200, 200) * 100)), 0, 1);
        }
        using var localImage = new MagickImage(basis.Pixels);
        OpsPresencePrototype.Apply(localImage, basis.Info, OpsArm.Locals, OpsClarity.Guided, field);
        MaxError(OpsPresenceOracle.Apply(source, w, h, w, OpsArm.Locals, OpsClarity.Guided, field),
            RenderPipelineTestSupport.ReadPixels(localImage), 1);
    }

    internal static void MaxError(ushort[] expected, ushort[] actual, int limit) =>
        Assert.InRange(expected.Zip(actual, (a, b) => Math.Abs(a - b)).Max(), 0, limit);
}

