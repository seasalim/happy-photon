using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class OpsDehazeRegressionTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NegativeToneInputsFollowEachRegimesBoundary(bool raw)
    {
        using var basis = RenderPipelineTestSupport.CreateBase([0, 0, 0], raw);
        var settings = new EditSettings { Brightness = 30, BaseLook = true };
        var tone = new OpsTone(basis.Info, settings);
        double[][] vectors = [[-.01, -.1, -1], [-.08, .03, .65], [-.001, .5, 1.4], [-3, .2, 2]];
        foreach (var vector in vectors)
        {
            var expected = DocumentedTone(vector, raw);
            var actual = tone.Extended(vector[0], vector[1], vector[2]);
            OpsKernelTests.MaxError(expected, [actual.R, actual.G, actual.B], 1);
            OpsKernelTests.MaxError(expected, OpsDehazeOracle.Tone(vector, basis.Info, settings), 1);
        }
        // Standard black: baseLook(0) + 30/100 * .35 = .117 -> 7668.
        // RAW non-positive inset values map to the log-window floor -> black;
        // Brightness and BaseLook are ignored by that regime.
        Assert.Equal(raw ? new ushort[3] : new ushort[] { 7668, 7668, 7668 }, DocumentedTone(vectors[0], raw));
        var signedRaw = DocumentedTone(vectors[1], true);
        var prematurelyClamped = DocumentedTone(vectors[1].Select(v => Math.Max(0, v)).ToArray(), true);
        Assert.True(signedRaw.Zip(prematurelyClamped, (a, b) => Math.Abs(a - b)).Max() > 100,
            "Mixed signed vector must distinguish RAW's post-inset boundary from a pre-inset clamp.");
    }

    public static IEnumerable<object[]> FractionalCases()
    {
        foreach (var (width, height) in new[] { (419, 283), (283, 419) })
        foreach (var raw in new[] { false, true })
        foreach (var amount in new[] { -100, -50, 50, 100 })
            yield return [width, height, raw, amount];
    }

    [Theory]
    [MemberData(nameof(FractionalCases))]
    public void FractionalLatticeAndMaterialRefinementMatchOracleEverywhere(int width, int height, bool raw, int amount)
    {
        var source = Edges(width, height);
        using var basis = RenderPipelineTestSupport.CreateBase(source, raw, height);
        var settings = new EditSettings { Brightness = 30, BaseLook = true,
            Wb = new() { Mode = WbMode.Picked, Gains = [1.2, .9, 1.1] } };
        var wb = RenderChromaticStage.CreateWhiteBalanceMatrix(basis.Info, settings);
        var prototype = OpsDehaze.Build(basis.Pixels, wb, 2);
        var oracle = OpsDehazeOracle.Build(source, width, height, wb);
        Assert.True(width > prototype.Transmission.Width && height > prototype.Transmission.Height);
        Assert.NotEqual(0, width % prototype.Transmission.Width);
        Assert.NotEqual(0, height % prototype.Transmission.Height);
        var expected = new[] { new ushort[source.Length], new ushort[source.Length] };
        double maxTransmissionDifference = 0, maxCorrectionDifference = 0;
        for (var i = 0; i < source.Length / 3; i++)
        {
            var value = new double[3];
            for (var c = 0; c < 3; c++) for (var j = 0; j < 3; j++)
                value[c] += wb[c, j] * source[i * 3 + j] / 65535d;
            var u = (i % width + .5) / width; var v = (i / width + .5) / height;
            maxTransmissionDifference = Math.Max(maxTransmissionDifference, Math.Abs(
                prototype.Read(u, v, value[0], value[1], value[2], true) -
                prototype.Read(u, v, value[0], value[1], value[2], false)));
            var plain = oracle.Correct(value, u, v, amount, false);
            var refined = oracle.Correct(value, u, v, amount, true);
            maxCorrectionDifference = Math.Max(maxCorrectionDifference, plain.Zip(refined, (a, b) => Math.Abs(a - b)).Max());
            OpsDehazeOracle.Tone(plain, basis.Info, settings).CopyTo(expected[0], i * 3);
            OpsDehazeOracle.Tone(refined, basis.Info, settings).CopyTo(expected[1], i * 3);
        }
        var rendered = new ushort[2][];
        var errors = new int[2];
        for (var mode = 0; mode < 2; mode++)
        {
            using var actual = new MagickImage(basis.Pixels);
            OpsRenderHarness.Fused(actual, basis.Info, settings, new("DH", Dehaze: amount), mode == 1, 2, 7);
            rendered[mode] = RenderPipelineTestSupport.ReadPixels(actual);
            Assert.Equal(expected[mode].Length, rendered[mode].Length);
            errors[mode] = expected[mode].Zip(rendered[mode], (a, b) => Math.Abs(a - b)).Max();
            Assert.InRange(errors[mode], 0, 1);
        }
        var maxCodeDifference = rendered[0].Zip(rendered[1], (a, b) => Math.Abs(a - b)).Max();
        output.WriteLine($"{width}x{height} raw={raw} amount={amount}: max oracle Q16 error lattice/refined={errors[0]}/{errors[1]}; " +
            $"max |refined-unrefined| transmission={maxTransmissionDifference:G9}, signed correction={maxCorrectionDifference:G9}, Q16={maxCodeDifference}");
        Assert.True(maxTransmissionDifference > .02, "Refinement must materially reweight neighbouring cells.");
        Assert.True(maxCorrectionDifference > .01, "Refinement must materially change the signed correction.");
        Assert.True(maxCodeDifference > 64, "Refinement must remain material after tone and Q16 encoding.");
    }

    private static ushort[] Edges(int width, int height)
    {
        // Slanted and horizontal chromatic steps cross fractional cells in both orientations.
        double[][] colors = [[.10, .18, .24], [.72, .75, .78], [.35, .22, .12], [.28, .48, .65]];
        var source = new ushort[width * height * 3];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var region = (x < .43 * width + .17 * y ? 0 : 1) + (y < .57 * height ? 0 : 2);
            for (var c = 0; c < 3; c++) source[(y * width + x) * 3 + c] =
                (ushort)Math.Round((colors[region][c] + .015 * x / width + .01 * y / height) * 65535);
        }
        return source;
    }

    // Independent equations from RENDER.md §5 and TONE_ENGINE.md §4, specialized
    // to identity curves, neutral tone sliders, Brightness +30 and BaseLook on.
    // No OpsTone, OpsDehazeOracle, ToneLut or AgxToneEngine calls/constants.
    private static ushort[] DocumentedTone(double[] value, bool raw)
    {
        if (!raw) return value.Select(v =>
        {
            var encoded = Encode(Math.Clamp(v, 0, 1));
            var looked = encoded + .012 * Math.Pow(1 - encoded, 3) -
                .4 * Math.Sin(2 * Math.PI * encoded) * encoded * (1 - encoded) - .03 * Math.Pow(encoded, 3);
            return Quantize(looked + .105);
        }).ToArray();
        double[,] inset = {
            { .9722125648757899, .0005798182049564, .0272076169192538 },
            { .0236356386540170, .8511231029574200, .1252412583885629 },
            { .0809977588044689, .0815268062292870, .8374754349662439 } };
        double[,] outset = {
            { 1.0313429748257903, .0025432830319416, -.0338862578577319 },
            { -.0141655132770723, 1.1919580980795306, -.1777925848024580 },
            { -.0983689754038757, -.1162810669486410, 1.2146500423525171 } };
        var toned = new double[3];
        var xp = 10 / 16.5; var yp = Math.Pow(.18, 1 / 2.2);
        for (var c = 0; c < 3; c++)
        {
            var scene = Enumerable.Range(0, 3).Sum(j => inset[c, j] * value[j]);
            var x = scene <= 0 ? 0 : Math.Clamp((Math.Log2(scene / .18) + 10) / 16.5, 0, 1);
            var lower = x < xp; var lx = lower ? xp : 1 - xp; var ly = lower ? yp : 1 - yp;
            var power = lower ? 3 : 3.25;
            var scale = 2 * lx / Math.Pow(Math.Pow(2 * lx / ly, power) - 1, 1 / power);
            var z = 2 * Math.Abs(x - xp) / scale;
            var tail = scale * z / Math.Pow(1 + Math.Pow(z, power), 1 / power);
            toned[c] = Math.Pow(Math.Clamp(yp + (lower ? -tail : tail), 0, 1), 2.2);
        }
        return Enumerable.Range(0, 3).Select(c => Quantize(Encode(Math.Clamp(
            Enumerable.Range(0, 3).Sum(j => outset[c, j] * toned[j]), 0, 1)))).ToArray();
    }

    private static double Encode(double v) => v <= .0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - .055;
    private static ushort Quantize(double v) => (ushort)Math.Round(Math.Clamp(v, 0, 1) * 65535, MidpointRounding.AwayFromZero);
}
