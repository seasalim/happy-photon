using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class AgxHighlightQualityTests
{
    private readonly ITestOutputHelper _output;

    public AgxHighlightQualityTests(ITestOutputHelper output) => _output = output;

    public static TheoryData<string, double, double, double> SweepPatches => new()
    {
        { "R", 0.18, 0, 0 },
        { "G", 0, 0.18, 0 },
        { "B", 0, 0, 0.18 },
        { "C", 0, 0.18, 0.18 },
        { "M", 0.18, 0, 0.18 },
        { "Y", 0.18, 0.18, 0 }
    };

    [Theory]
    [MemberData(nameof(SweepPatches))]
    public void ExposureSweep_PreservesHueAndDesaturatesHighlights(
        string label,
        double red,
        double green,
        double blue)
    {
        var baseLinearSrgb = new AgxRgb(red, green, blue);
        var base2020 = AgxBlenderOracleTests.Transform(
            RgbColorSpaceMatrices.LinearSrgbToLinearRec2020,
            baseLinearSrgb);
        var observations = new List<(double Exposure, Oklab Lab)>();
        for (var step = 0; step <= 12; step++)
        {
            var exposure = step * 0.5;
            var encoded2020 = AgxCrossing.TransformAnalytic(
                base2020,
                AgxToneEnginePropertyTests.Parameters(exposureEv: exposure));
            var linear2020 = new AgxRgb(
                ToneLut.SrgbDecode(encoded2020.Red),
                ToneLut.SrgbDecode(encoded2020.Green),
                ToneLut.SrgbDecode(encoded2020.Blue));
            var linearSrgb = Clamp(AgxBlenderOracleTests.Transform(
                RgbColorSpaceMatrices.LinearRec2020ToLinearSrgb,
                linear2020));
            observations.Add((exposure, ToOklab(linearSrgb)));
        }

        var reference = observations[0].Lab;
        var maxHueDrift = 0.0;
        foreach (var observation in observations)
        {
            if (reference.Chroma >= 0.02 && observation.Lab.Chroma >= 0.02)
            {
                var drift = HueDistance(reference.Hue, observation.Lab.Hue);
                maxHueDrift = Math.Max(maxHueDrift, drift);
            }
        }

        var overRange = observations
            .Where(item => 0.18 * Math.Pow(2, item.Exposure) > 1)
            .ToArray();
        var chromaMonotone = true;
        for (var index = 1; index < overRange.Length; index++)
        {
            chromaMonotone &= overRange[index].Lab.Chroma <=
                overRange[index - 1].Lab.Chroma + 1e-12;
        }
        var retainedChroma = observations[^1].Lab.Chroma / reference.Chroma;
        _output.WriteLine(
            $"{label}: max hue drift {maxHueDrift:F3} degrees; " +
            $"+6 EV chroma {retainedChroma:P2}; " +
            $"over-range monotone {chromaMonotone}.");

        Assert.True(
            maxHueDrift <= 8,
            $"{label} max hue drift {maxHueDrift:F3} degrees exceeds 8 degrees.");
        Assert.True(chromaMonotone, $"{label} over-range chroma was not monotone.");
        Assert.True(
            retainedChroma <= 0.65,
            $"{label}@+6 EV retained {retainedChroma:P2} of base chroma.");
    }

    private static AgxRgb Clamp(AgxRgb value) =>
        new(
            Math.Clamp(value.Red, 0, 1),
            Math.Clamp(value.Green, 0, 1),
            Math.Clamp(value.Blue, 0, 1));

    private static Oklab ToOklab(AgxRgb rgb)
    {
        var l = Math.Cbrt(
            0.4122214708 * rgb.Red +
            0.5363325363 * rgb.Green +
            0.0514459929 * rgb.Blue);
        var m = Math.Cbrt(
            0.2119034982 * rgb.Red +
            0.6806995451 * rgb.Green +
            0.1073969566 * rgb.Blue);
        var s = Math.Cbrt(
            0.0883024619 * rgb.Red +
            0.2817188376 * rgb.Green +
            0.6299787005 * rgb.Blue);
        var a = 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s;
        var b = 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s;
        var hue = Math.Atan2(b, a) * 180 / Math.PI;
        if (hue < 0)
        {
            hue += 360;
        }
        return new Oklab(Math.Sqrt(a * a + b * b), hue);
    }

    private static double HueDistance(double first, double second)
    {
        var difference = Math.Abs(first - second);
        return Math.Min(difference, 360 - difference);
    }

    private readonly record struct Oklab(double Chroma, double Hue);
}
