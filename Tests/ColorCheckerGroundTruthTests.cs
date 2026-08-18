using System.Security.Cryptography;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public static class ColorCheckerTestCollection
{
    public const string Name = "ColorChecker ground truth";
}

[CollectionDefinition(ColorCheckerTestCollection.Name, DisableParallelization = true)]
public sealed class ColorCheckerTestCollectionDefinition;

[Collection(ColorCheckerTestCollection.Name)]
public sealed class ColorCheckerGroundTruthTests
{
    private static readonly Lazy<ColorCheckerMeasurement> Measurement =
        new(Measure, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly ITestOutputHelper _output;

    public ColorCheckerGroundTruthTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ColorChecker_MeanDeltaE00_IsWithinMeasuredBudget()
    {
        var measurement = Measurement.Value;
        ReportPatches(measurement);

        Assert.True(
            measurement.MeanDeltaE00 <= measurement.Manifest.Budget.MeanDeltaE00,
            $"ColorChecker mean ΔE00 {measurement.MeanDeltaE00:F4} exceeds " +
            $"the measured budget {measurement.Manifest.Budget.MeanDeltaE00:F1}.");
    }

    [Fact]
    public void ColorChecker_EveryPatchDeltaE00_IsWithinMeasuredBudget()
    {
        var measurement = Measurement.Value;
        ReportPatches(measurement);

        Assert.True(
            measurement.MaximumDeltaE00 <=
                measurement.Manifest.Budget.MaximumPatchDeltaE00,
            $"ColorChecker maximum patch ΔE00 {measurement.MaximumDeltaE00:F4} " +
            $"exceeds the measured budget " +
            $"{measurement.Manifest.Budget.MaximumPatchDeltaE00:F1}.");
    }

    [Fact]
    public void ColorChecker_FreshDecodeNeutralCalibrationHasNotDrifted()
    {
        var measurement = Measurement.Value;

        Assert.True(
            measurement.MaximumNeutralXyzDrift <=
                measurement.Manifest.Calibration.FreshDecodeXyzMaxAbsoluteDrift,
            $"Fresh neutral XYZ drift {measurement.MaximumNeutralXyzDrift:R} exceeds " +
            $"{measurement.Manifest.Calibration.FreshDecodeXyzMaxAbsoluteDrift:R}.");
        Assert.InRange(
            measurement.FreshExposureScalar,
            measurement.Manifest.Calibration.ExposureScalar - 0.005,
            measurement.Manifest.Calibration.ExposureScalar + 0.005);
    }

    private void ReportPatches(ColorCheckerMeasurement measurement)
    {
        foreach (var patch in measurement.Patches)
        {
            _output.WriteLine(
                $"ColorChecker patch {patch.Index + 1:00} {patch.Name}: " +
                $"ΔE00={patch.DeltaE00:F4}; " +
                $"measured Lab=({patch.Measured.L:F3}, {patch.Measured.A:F3}, " +
                $"{patch.Measured.B:F3})");
        }
        _output.WriteLine(
            $"ColorChecker aggregate: mean ΔE00={measurement.MeanDeltaE00:F4}; " +
            $"maximum ΔE00={measurement.MaximumDeltaE00:F4}.");
        _output.WriteLine(
            $"ColorChecker exact aggregate: mean={measurement.MeanDeltaE00:R}; " +
            $"maximum={measurement.MaximumDeltaE00:R}.");
    }

    private static ColorCheckerMeasurement Measure()
    {
        var manifest = ColorCheckerManifest.Load();
        var oracle = ColorScienceOracleData.Load();
        var fixturePath = Path.Combine(
            GoldenTestPaths.AssetDirectory,
            manifest.Fixture.FileName);
        AssertFixtureIdentity(fixturePath, manifest.Fixture);

        var priorThreads = Environment.GetEnvironmentVariable(
            manifest.RenderPath.OpenMpEnvironmentVariable);
        Environment.SetEnvironmentVariable(
            manifest.RenderPath.OpenMpEnvironmentVariable,
            manifest.RenderPath.OpenMpValue);
        try
        {
            return MeasureCore(fixturePath, manifest, oracle);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                manifest.RenderPath.OpenMpEnvironmentVariable,
                priorThreads);
        }
    }

    private static ColorCheckerMeasurement MeasureCore(
        string fixturePath,
        ColorCheckerManifest manifest,
        ColorScienceOracleData oracle)
    {
        var space = oracle.Space("linear-srgb-d65");
        var rgbToXyz = ColorScienceMatrixAssertions.DeriveRgbToXyz(
            space.Primaries,
            space.WhitePoint);
        using var baseImage = new RawBaseLoader().LoadFullBase(
            new ImageFile(fixturePath),
            BaseDecodeSettings.Default,
            CancellationToken.None) ?? throw new InvalidOperationException(
                "The ColorChecker fixture did not decode.");
        Assert.Equal((uint)manifest.RenderPath.ExpectedWidth, baseImage.Pixels.Width);
        Assert.Equal((uint)manifest.RenderPath.ExpectedHeight, baseImage.Pixels.Height);

        var basePatches = ColorCheckerSampling.SampleXyz(
            baseImage.Pixels,
            manifest.Geometry,
            rgbToXyz,
            decodeSrgb: false);
        var maximumDrift = MeasureNeutralDrift(
            basePatches,
            manifest.Calibration);
        var gains = DeriveWorkingSpaceGains(
            manifest.Calibration.FrozenNeutralSamplesXyzD65,
            PrecisionColorCases.Invert(rgbToXyz));
        AssertVectorClose(
            gains,
            manifest.Calibration.MeasuredLinearSrgbGains,
            2e-12);

        var settings = new EditSettings
        {
            Wb = new WhiteBalanceSettings
            {
                Mode = WbMode.Picked,
                Gains = gains
            }
        };
        using var rendered = new RenderPipeline().Render(new RenderRequest(
            baseImage,
            settings,
            RenderIntent.Export,
            MaxDimension: null,
            new RenderOptions(false, false)));
        Assert.Equal((uint)manifest.RenderPath.ExpectedWidth, rendered.Image.Width);
        Assert.Equal((uint)manifest.RenderPath.ExpectedHeight, rendered.Image.Height);

        var renderedD65 = ColorCheckerSampling.SampleXyz(
            rendered.Image,
            manifest.Geometry,
            rgbToXyz,
            decodeSrgb: true);
        var d65ToReference = ColorScienceMatrixAssertions.ToMatrix(
            oracle.Adaptation("bradford-d65-to-d50").Matrix);
        var patches = new ColorCheckerPatchMeasurement[24];
        for (var index = 0; index < patches.Length; index++)
        {
            var scaledD65 = renderedD65[index].Xyz
                .Select(value => value * manifest.Calibration.ExposureScalar)
                .ToArray();
            var referenceXyz = PrecisionColorCases.Transform(
                d65ToReference,
                scaledD65);
            var measuredLab = ToLab(referenceXyz, oracle.ColorChecker.ReferenceWhiteXyz);
            var reference = oracle.ColorChecker.Patches[index];
            var referenceLab = new PrecisionLab(
                reference.Lab[0], reference.Lab[1], reference.Lab[2]);
            patches[index] = new ColorCheckerPatchMeasurement(
                index,
                reference.Name,
                measuredLab,
                PrecisionDeltaE.Ciede2000(measuredLab, referenceLab));
        }

        var freshScalar = CalculateExposureScalar(
            renderedD65,
            d65ToReference,
            oracle.ColorChecker,
            manifest.Calibration.NeutralPatchIndices);
        return new ColorCheckerMeasurement(
            manifest,
            patches,
            patches.Average(patch => patch.DeltaE00),
            patches.Max(patch => patch.DeltaE00),
            maximumDrift,
            freshScalar);
    }

    private static double MeasureNeutralDrift(
        ColorCheckerPatchSample[] fresh,
        ColorCheckerCalibration calibration)
    {
        var maximum = 0.0;
        foreach (var frozen in calibration.FrozenNeutralSamplesXyzD65)
        {
            Assert.False(fresh[frozen.PatchIndex].ContainsClippedSample,
                $"Neutral patch {frozen.PatchIndex} contains a clipped base sample.");
            for (var channel = 0; channel < 3; channel++)
            {
                maximum = Math.Max(maximum,
                    Math.Abs(fresh[frozen.PatchIndex].Xyz[channel] - frozen.Xyz[channel]));
            }
        }
        return maximum;
    }

    private static double[] DeriveWorkingSpaceGains(
        FrozenNeutralXyz[] frozen,
        double[,] xyzToWorking)
    {
        var samples = frozen
            .Select(value => PrecisionColorCases.Transform(xyzToWorking, value.Xyz))
            .ToArray();
        var means = Enumerable.Range(0, 3)
            .Select(channel => samples.Average(sample => sample[channel]))
            .ToArray();
        return [means[1] / means[0], 1, means[1] / means[2]];
    }

    private static double CalculateExposureScalar(
        ColorCheckerPatchSample[] measuredD65,
        double[,] d65ToReference,
        OracleColorChecker checker,
        int[] neutralIndices)
    {
        var measuredY = neutralIndices.Select(index =>
            PrecisionColorCases.Transform(d65ToReference, measuredD65[index].Xyz)[1])
            .ToArray();
        var referenceY = neutralIndices.Select(index => checker.Patches[index].Xyz[1])
            .ToArray();
        return measuredY.Zip(referenceY, (measured, reference) => measured * reference).Sum() /
            measuredY.Sum(value => value * value);
    }

    private static PrecisionLab ToLab(double[] xyz, double[] white)
    {
        var fx = LabPivot(xyz[0] / white[0]);
        var fy = LabPivot(xyz[1] / white[1]);
        var fz = LabPivot(xyz[2] / white[2]);
        return new PrecisionLab(
            116 * fy - 16,
            500 * (fx - fy),
            200 * (fy - fz));
    }

    private static double LabPivot(double value) => value > 216.0 / 24389
        ? Math.Cbrt(value)
        : 841.0 / 108 * value + 4.0 / 29;

    private static void AssertFixtureIdentity(string path, ColorCheckerFixture fixture)
    {
        var info = new FileInfo(path);
        Assert.True(info.Exists, $"ColorChecker fixture is missing: {path}");
        Assert.Equal(fixture.ByteLength, info.Length);
        using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        Assert.Equal(fixture.Sha256, hash);
    }

    private static void AssertVectorClose(
        double[] actual,
        double[] expected,
        double tolerance)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < actual.Length; index++)
        {
            Assert.InRange(actual[index], expected[index] - tolerance,
                expected[index] + tolerance);
        }
    }
}

internal sealed record ColorCheckerMeasurement(
    ColorCheckerManifest Manifest,
    ColorCheckerPatchMeasurement[] Patches,
    double MeanDeltaE00,
    double MaximumDeltaE00,
    double MaximumNeutralXyzDrift,
    double FreshExposureScalar);

internal sealed record ColorCheckerPatchMeasurement(
    int Index,
    string Name,
    PrecisionLab Measured,
    double DeltaE00);
