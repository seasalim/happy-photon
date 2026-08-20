using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class AgxBlenderOracleTests
{
    private readonly ITestOutputHelper _output;

    public AgxBlenderOracleTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void NeutralCrossing_MeetsFrozenBlenderOracleTolerances()
    {
        var oracle = LoadOracle();
        Assert.Equal("4.5.12 LTS", oracle.Blender);
        Assert.Equal("84afd5f785f7", oracle.BuildHash);
        Assert.Equal("sRGB", oracle.Display);
        Assert.Equal("AgX", oracle.View);
        Assert.Equal("None", oracle.Look);
        Assert.Equal(0, oracle.Exposure);
        Assert.Equal(1, oracle.Gamma);
        Assert.Equal("Linear Rec.709", oracle.InputColorSpace);
        Assert.Equal(16, oracle.PngDepth);
        Assert.Equal(112, oracle.Vectors.Count);

        var neutral = new List<double>();
        var chromatic = new List<double>();
        double? grey = null;
        foreach (var vector in oracle.Vectors)
        {
            var exposure = ParseExposure(vector.Label);
            var gain = Math.Pow(2, exposure);
            var base709 = new AgxRgb(
                vector.Linear709In[0] / gain,
                vector.Linear709In[1] / gain,
                vector.Linear709In[2] / gain);
            var base2020 = Transform(
                RgbColorSpaceMatrices.LinearSrgbToLinearRec2020,
                base709);
            var oursEncoded2020 = AgxCrossing.TransformAnalytic(
                base2020,
                AgxToneEnginePropertyTests.Parameters(exposureEv: exposure));
            var oursLinearSrgb = Transform(
                RgbColorSpaceMatrices.LinearRec2020ToLinearSrgb,
                Decode(oursEncoded2020));
            oursLinearSrgb = Clamp(oursLinearSrgb);
            var blenderLinearSrgb = new AgxRgb(
                ToneLut.SrgbDecode(vector.DisplayCodeOut[0]),
                ToneLut.SrgbDecode(vector.DisplayCodeOut[1]),
                ToneLut.SrgbDecode(vector.DisplayCodeOut[2]));
            var deltaE = PrecisionDeltaE.Ciede2000(
                ToLab(blenderLinearSrgb),
                ToLab(oursLinearSrgb));

            if (vector.Label.StartsWith("N@", StringComparison.Ordinal))
            {
                neutral.Add(deltaE);
                if (vector.Label == "N@+0.0")
                {
                    grey = deltaE;
                }
            }
            else
            {
                chromatic.Add(deltaE);
            }
        }

        var neutralStats = Statistics(neutral);
        var chromaticStats = Statistics(chromatic);
        _output.WriteLine(
            $"Neutral: mean {neutralStats.Mean:F3}, " +
            $"p99 {neutralStats.P99:F3}, max {neutralStats.Max:F3}.");
        _output.WriteLine(
            $"Chromatic: mean {chromaticStats.Mean:F3}, " +
            $"p99 {chromaticStats.P99:F3}, max {chromaticStats.Max:F3}.");
        _output.WriteLine($"Middle grey: {grey:F4} DeltaE00.");

        Assert.InRange(neutralStats.Mean, 0, 0.75);
        Assert.InRange(neutralStats.P99, 0, 2.1);
        Assert.InRange(neutralStats.Max, 0, 2.2);
        Assert.InRange(chromaticStats.Mean, 0, 5.0);
        Assert.InRange(chromaticStats.P99, 0, 12.0);
        Assert.InRange(chromaticStats.Max, 0, 13.0);
        Assert.NotNull(grey);
        Assert.InRange(grey.Value, 0, 0.05);
    }

    private static AgxOracle LoadOracle()
    {
        var path = Path.Combine(
            GoldenTestPaths.AssetDirectory,
            "agx-blender-oracle.json");
        return JsonSerializer.Deserialize<AgxOracle>(File.ReadAllText(path)) ??
            throw new InvalidOperationException($"Invalid AgX oracle: {path}");
    }

    private static double ParseExposure(string label)
    {
        var separator = label.IndexOf('@');
        if (separator < 0 ||
            !double.TryParse(
                label.AsSpan(separator + 1),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var exposure))
        {
            throw new InvalidOperationException($"Invalid oracle label: {label}");
        }
        return exposure;
    }

    private static AgxRgb Decode(AgxRgb encoded) =>
        new(
            ToneLut.SrgbDecode(encoded.Red),
            ToneLut.SrgbDecode(encoded.Green),
            ToneLut.SrgbDecode(encoded.Blue));

    private static AgxRgb Clamp(AgxRgb value) =>
        new(
            Math.Clamp(value.Red, 0, 1),
            Math.Clamp(value.Green, 0, 1),
            Math.Clamp(value.Blue, 0, 1));

    internal static AgxRgb Transform(double[,] matrix, AgxRgb value) =>
        new(
            matrix[0, 0] * value.Red + matrix[0, 1] * value.Green +
                matrix[0, 2] * value.Blue,
            matrix[1, 0] * value.Red + matrix[1, 1] * value.Green +
                matrix[1, 2] * value.Blue,
            matrix[2, 0] * value.Red + matrix[2, 1] * value.Green +
                matrix[2, 2] * value.Blue);

    private static PrecisionLab ToLab(AgxRgb linearSrgb)
    {
        const double d65X = 0.95047;
        const double d65Y = 1.0;
        const double d65Z = 1.08883;
        var matrix = RgbColorSpaceMatrices.LinearSrgbToXyzD65DerivedExact;
        var xyz = Transform(matrix, linearSrgb);
        var fx = PivotXyz(xyz.Red / d65X);
        var fy = PivotXyz(xyz.Green / d65Y);
        var fz = PivotXyz(xyz.Blue / d65Z);
        return new PrecisionLab(
            116 * fy - 16,
            500 * (fx - fy),
            200 * (fy - fz));
    }

    private static double PivotXyz(double value) =>
        value > 216.0 / 24389
            ? Math.Cbrt(value)
            : 841.0 / 108 * value + 4.0 / 29;

    private static (double Mean, double P99, double Max) Statistics(
        List<double> values)
    {
        var ordered = values.Order().ToArray();
        var position = 0.99 * (ordered.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(lower + 1, ordered.Length - 1);
        var fraction = position - lower;
        var p99 = ordered[lower] +
            (ordered[upper] - ordered[lower]) * fraction;
        return (values.Average(), p99, ordered[^1]);
    }

    private sealed record AgxOracle(
        [property: JsonPropertyName("blender")] string Blender,
        [property: JsonPropertyName("build_hash")] string BuildHash,
        [property: JsonPropertyName("display")] string Display,
        [property: JsonPropertyName("view")] string View,
        [property: JsonPropertyName("look")] string Look,
        [property: JsonPropertyName("exposure")] double Exposure,
        [property: JsonPropertyName("gamma")] double Gamma,
        [property: JsonPropertyName("input_colorspace")] string InputColorSpace,
        [property: JsonPropertyName("png_depth")] int PngDepth,
        [property: JsonPropertyName("vectors")] List<AgxOracleVector> Vectors);

    private sealed record AgxOracleVector(
        [property: JsonPropertyName("label")] string Label,
        [property: JsonPropertyName("linear_709_in")] double[] Linear709In,
        [property: JsonPropertyName("display_code_out")] double[] DisplayCodeOut);
}
