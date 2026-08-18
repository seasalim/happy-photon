using System.Globalization;
using System.Text;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

internal static class PrecisionColorCases
{
    // Published matrices use different rounding precisions. Do not report their
    // neutral-white closure error as a Q16 range clip.
    private const double MatrixReferenceEdgeTolerance = 0.001;

    internal static readonly double[,] SrgbToXyzD65 =
        RgbColorSpaceMatrices.LinearSrgbToXyzD65DerivedExact;

    internal static readonly double[,] Rec2020ToXyzD65 =
        RgbColorSpaceMatrices.LinearRec2020ToXyzD65DerivedExact;

    internal static readonly double[,] BradfordD65ToD50 =
    {
        { 1.0479298208405488, 0.0229467933410191, -0.0501922295431356 },
        { 0.0296278156881593, 0.9904344845732490, -0.0170738250293851 },
        { -0.0092430581525912, 0.0150551448965779, 0.7518742814281370 }
    };

    internal static readonly double[,] RommToXyzD50 =
    {
        { 0.7976749, 0.1351917, 0.0313534 },
        { 0.2880402, 0.7118741, 0.0000857 },
        { 0.0000000, 0.0000000, 0.8252100 }
    };

    public static void RunWidePrimaries(
        StringBuilder payload,
        PrecisionCensusManifest manifest)
    {
        var population = manifest.Population("wide-space-representative-colors");
        AppendDefinitions(payload);
        RunSpace(
            payload,
            population,
            "rec2020-linear-d65",
            Multiply(Invert(Rec2020ToXyzD65), SrgbToXyzD65),
            manifest.WideColors);
        RunSpace(
            payload,
            population,
            "romm-linear-d50",
            Multiply(
                Invert(RommToXyzD50),
                Multiply(BradfordD65ToD50, SrgbToXyzD65)),
            manifest.WideColors);
    }

    private static void RunSpace(
        StringBuilder payload,
        PrecisionPopulationManifest population,
        string space,
        double[,] toWide,
        IReadOnlyList<PrecisionWideColorManifest> colors)
    {
        var fromWide = Invert(toWide);
        var referenceEncoded = new double[colors.Count * 3];
        var actualEncoded = new double[colors.Count * 3];
        var clippedPixels = new bool[colors.Count];
        var negative = 0;
        var above = 0;
        for (var pixel = 0; pixel < colors.Count; pixel++)
        {
            var source = colors[pixel].LinearSrgb;
            var wide = Transform(toWide, source);
            var stored = new double[3];
            for (var channel = 0; channel < 3; channel++)
            {
                negative += wide[channel] < -MatrixReferenceEdgeTolerance ? 1 : 0;
                above += wide[channel] > 1 + MatrixReferenceEdgeTolerance ? 1 : 0;
                clippedPixels[pixel] |=
                    wide[channel] < -MatrixReferenceEdgeTolerance ||
                    wide[channel] > 1 + MatrixReferenceEdgeTolerance;
                stored[channel] = Math.Round(
                    Math.Clamp(wide[channel], 0, 1) * ushort.MaxValue,
                    MidpointRounding.AwayFromZero) / ushort.MaxValue;
            }
            var roundTrip = Transform(fromWide, stored);
            for (var channel = 0; channel < 3; channel++)
            {
                var offset = pixel * 3 + channel;
                referenceEncoded[offset] = SrgbEncode(source[channel]);
                actualEncoded[offset] = SrgbEncode(
                    Math.Clamp(roundTrip[channel], 0, 1));
            }
        }

        var quality = PrecisionBoundaryCensus.AnalyzePhotographicQuality(
            actualEncoded,
            referenceEncoded,
            colors.Count,
            1,
            clippedPixels);
        payload.Append("CENSUS_POPULATION case=case-2-wide-primaries")
            .Append(" id=").Append(population.Id)
            .Append(" kind=").Append(population.Kind)
            .Append(" rowSemantics=").Append(population.RowSemantics)
            .Append(" intensity=").Append(population.Intensity).AppendLine();
        payload.Append("CENSUS_WIDE case=case-2-wide-primaries")
            .Append(" population=").Append(population.Id)
            .Append(" space=").Append(space)
            .Append(" operation=pure-encoding-round-trip")
            .Append(" pipelineStagesRun=0")
            .Append(" transfer=linear")
            .Append(" matrixReferenceEdgeTolerance=0.001")
            .Append(" channelSamples=").Append(colors.Count * 3)
            .Append(" negativeClips=").Append(negative)
            .Append(" aboveWhiteClips=").Append(above)
            .Append(" clipBasis=exact-full-population")
            .Append(" plannedStageContractLoss=")
            .Append(negative + above > 0 ? "true" : "false")
            .AppendLine();
        PrecisionEvidenceReport.AppendQuality(
            payload,
            "case-2-wide-primaries",
            population.Id,
            space,
            quality,
            phaseZeroThresholdCrossed: false,
            plannedStageContractLoss: negative + above > 0);
    }

    private static void AppendDefinitions(StringBuilder payload)
    {
        payload.AppendLine(
            "CENSUS_COLOR_SCIENCE spaces=linear-srgb,linear-rec2020,linear-romm " +
            "srgbWhite=D65 rec2020White=D65 rommWhite=D50 " +
            "adaptation=Bradford-D65-to-D50 deltaE=CIEDE2000 " +
            "comparisonWhite=CIE-D65-2-degree-observer deltaEInput=encoded-srgb");
        payload.AppendLine(
            "CENSUS_CHROMATICITIES space=linear-srgb " +
            "r=0.6400,0.3300 g=0.3000,0.6000 b=0.1500,0.0600 w=0.3127,0.3290");
        payload.AppendLine(
            "CENSUS_CHROMATICITIES space=linear-rec2020 " +
            "r=0.7080,0.2920 g=0.1700,0.7970 b=0.1310,0.0460 w=0.3127,0.3290");
        payload.AppendLine(
            "CENSUS_CHROMATICITIES space=linear-romm " +
            "r=0.7347,0.2653 g=0.1596,0.8404 b=0.0366,0.0001 w=0.3457,0.3585");
        AppendMatrix(payload, "srgb-to-xyz-d65", SrgbToXyzD65);
        AppendMatrix(payload, "rec2020-to-xyz-d65", Rec2020ToXyzD65);
        AppendMatrix(payload, "bradford-d65-to-d50", BradfordD65ToD50);
        AppendMatrix(payload, "romm-to-xyz-d50", RommToXyzD50);
    }

    private static void AppendMatrix(
        StringBuilder payload,
        string name,
        double[,] matrix)
    {
        payload.Append("CENSUS_MATRIX name=").Append(name).Append(" values=");
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                if (row != 0 || column != 0)
                {
                    payload.Append(',');
                }
                payload.Append(matrix[row, column].ToString(
                    "R", CultureInfo.InvariantCulture));
            }
        }
        payload.AppendLine();
    }

    internal static double[] Transform(double[,] matrix, double[] value) =>
    [
        matrix[0, 0] * value[0] + matrix[0, 1] * value[1] + matrix[0, 2] * value[2],
        matrix[1, 0] * value[0] + matrix[1, 1] * value[1] + matrix[1, 2] * value[2],
        matrix[2, 0] * value[0] + matrix[2, 1] * value[1] + matrix[2, 2] * value[2]
    ];

    internal static double[,] Multiply(double[,] left, double[,] right)
    {
        var result = new double[3, 3];
        for (var row = 0; row < 3; row++)
        for (var column = 0; column < 3; column++)
        for (var inner = 0; inner < 3; inner++)
        {
            result[row, column] += left[row, inner] * right[inner, column];
        }
        return result;
    }

    internal static double[,] Invert(double[,] value)
    {
        var a = value[0, 0]; var b = value[0, 1]; var c = value[0, 2];
        var d = value[1, 0]; var e = value[1, 1]; var f = value[1, 2];
        var g = value[2, 0]; var h = value[2, 1]; var i = value[2, 2];
        var determinant = a * (e * i - f * h) - b * (d * i - f * g) +
            c * (d * h - e * g);
        return new[,]
        {
            { (e * i - f * h) / determinant, (c * h - b * i) / determinant, (b * f - c * e) / determinant },
            { (f * g - d * i) / determinant, (a * i - c * g) / determinant, (c * d - a * f) / determinant },
            { (d * h - e * g) / determinant, (b * g - a * h) / determinant, (a * e - b * d) / determinant }
        };
    }

    private static double SrgbEncode(double value) => value <= 0.0031308
        ? 12.92 * value
        : 1.055 * Math.Pow(value, 1 / 2.4) - 0.055;
}

public sealed class PrecisionColorCasesTests
{
    [Fact]
    public void PublishedSrgbMatrix_AgreesWithDerivationAndOracle() =>
        ColorScienceMatrixAssertions.AssertPublishedAndOracle(
            PrecisionColorCases.SrgbToXyzD65,
            "linear-srgb-d65",
            2e-12);

    [Fact]
    public void WideRoundTrip_DoesNotCallPublishedMatrixClosureAClip()
    {
        var payload = new StringBuilder();

        PrecisionColorCases.RunWidePrimaries(
            payload,
            PrecisionCensusManifest.Load());

        var plannedLossRows = payload.ToString()
            .Split('\n')
            .Where(line => line.Contains(
                "plannedStageContractLoss=true",
                StringComparison.Ordinal))
            .ToArray();
        Assert.True(plannedLossRows.Length == 0, string.Join('\n', plannedLossRows));
    }

    [Fact]
    public void PublishedSrgbToRec2020RedVector_IsPinned()
    {
        var conversion = PrecisionColorCases.Multiply(
            PrecisionColorCases.Invert(PrecisionColorCases.Rec2020ToXyzD65),
            PrecisionColorCases.SrgbToXyzD65);

        var actual = PrecisionColorCases.Transform(conversion, [1, 0, 0]);
        Assert.Equal(0.627403896, actual[0], 8);
        Assert.Equal(0.069097289, actual[1], 8);
        Assert.Equal(0.016391439, actual[2], 8);
    }

    [Fact]
    public void PublishedBradfordD65WhiteToD50White_IsPinned()
    {
        var actual = PrecisionColorCases.Transform(
            PrecisionColorCases.BradfordD65ToD50,
            [0.9504559271, 1.0, 1.0890577508]);

        Assert.Equal(0.964295666, actual[0], 8);
        Assert.Equal(1.000000036, actual[1], 8);
        Assert.Equal(0.825104539, actual[2], 8);
    }

    [Fact]
    public void PublishedLinearRommRedPrimary_IsPinned()
    {
        var actual = PrecisionColorCases.Transform(
            PrecisionColorCases.RommToXyzD50,
            [1, 0, 0]);

        Assert.Equal(0.7976749, actual[0], 7);
        Assert.Equal(0.2880402, actual[1], 7);
        Assert.Equal(0, actual[2], 7);
    }
}
