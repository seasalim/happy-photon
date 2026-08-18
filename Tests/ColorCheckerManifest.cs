using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace HappyPhoton.Tests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ColorCheckerManifest(
    int SchemaVersion,
    ColorCheckerFixture Fixture,
    ColorCheckerRenderPath RenderPath,
    ColorCheckerGeometry Geometry,
    ColorCheckerCalibration Calibration,
    ColorCheckerBudget Budget)
{
    public static ColorCheckerManifest Load()
    {
        var path = Path.Combine(
            GoldenTestPaths.AssetDirectory,
            "nikon-d300-colorchecker.json");
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        var manifest = JsonSerializer.Deserialize<ColorCheckerManifest>(
            File.ReadAllBytes(path), options) ?? throw new InvalidOperationException(
                "The ColorChecker manifest was empty.");
        manifest.Validate();
        return manifest;
    }

    private void Validate()
    {
        Assert.Equal(1, SchemaVersion);
        Assert.Equal(4, Geometry.Columns);
        Assert.Equal(6, Geometry.Rows);
        Assert.Equal(4, Geometry.CornersClockwiseFromTopLeft.Length);
        Assert.Equal(Geometry.Rows, Geometry.PatchIndexByImageCell.Length);
        Assert.All(Geometry.PatchIndexByImageCell,
            row => Assert.Equal(Geometry.Columns, row.Length));
        Assert.Equal(Enumerable.Range(0, 24),
            Geometry.PatchIndexByImageCell.SelectMany(row => row).Order());
        Assert.Equal([18, 19, 20, 21, 22], Calibration.NeutralPatchIndices);
        Assert.Equal(Calibration.NeutralPatchIndices,
            Calibration.FrozenNeutralSamplesXyzD65.Select(value => value.PatchIndex));
        Assert.True(Budget.MeanDeltaE00 > 0);
        Assert.True(Budget.MaximumPatchDeltaE00 > Budget.MeanDeltaE00);
    }
}

internal sealed record ColorCheckerFixture(
    string FileName,
    long ByteLength,
    string Sha256,
    string Camera,
    string Captured,
    string Lighting,
    int Iso,
    string Aperture,
    bool AuthorCapture,
    string License);

internal sealed record ColorCheckerRenderPath(
    string Base,
    string Intent,
    int? MaxDimension,
    int ExpectedWidth,
    int ExpectedHeight,
    string DecodeSettings,
    string OpenMpEnvironmentVariable,
    string OpenMpValue,
    string Settings);

internal sealed record ColorCheckerGeometry(
    int Rows,
    int Columns,
    double CentralInsetFraction,
    ColorCheckerPoint[] CornersClockwiseFromTopLeft,
    int[][] PatchIndexByImageCell);

internal sealed record ColorCheckerPoint(double X, double Y);

internal sealed record ColorCheckerCalibration(
    int[] NeutralPatchIndices,
    int DarkestNeutralExcluded,
    string ClipRule,
    string AggregationRule,
    FrozenNeutralXyz[] FrozenNeutralSamplesXyzD65,
    string WorkingSpaceGainRule,
    double[] MeasuredLinearSrgbGains,
    double FreshDecodeXyzMaxAbsoluteDrift,
    double ExposureScalar,
    string ExposureScalarRule);

internal sealed record FrozenNeutralXyz(int PatchIndex, double[] Xyz);

internal sealed record ColorCheckerBudget(
    string[] Statistics,
    string BoundRule,
    double MeanDeltaE00,
    double MaximumPatchDeltaE00,
    ColorCheckerObservation[] Observations,
    string[] PendingRidObservations);

internal sealed record ColorCheckerObservation(
    string Rid,
    int FreshProcessRuns,
    int OpenMpThreads,
    double[] MeanDeltaE00,
    double[] MaximumPatchDeltaE00);
