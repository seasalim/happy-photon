using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace HappyPhoton.Tests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record PrecisionCensusManifest(
    int SchemaVersion,
    PrecisionRetentionManifest Retention,
    string[] ExpectedCases,
    PrecisionPopulationManifest[] Populations,
    PrecisionRawAssetManifest[] FullFrameAssets,
    PrecisionExclusionManifest[] ExcludedAssets,
    PrecisionRawSettingManifest[] RawSettings,
    PrecisionRoiManifest[] FocusedRois,
    PrecisionWideColorManifest[] WideColors,
    PrecisionExposureManifest[] ExposureSettings,
    PrecisionStackedManifest StackedSettings)
{
    public string Digest { get; private init; } = string.Empty;

    public PrecisionPopulationManifest Population(string id) =>
        Populations.Single(population => population.Id == id);

    public static PrecisionCensusManifest Load()
    {
        var path = Path.Combine(
            GoldenTestPaths.AssetDirectory,
            "precision-census-manifest.json");
        var bytes = File.ReadAllBytes(path);
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        var manifest = JsonSerializer.Deserialize<PrecisionCensusManifest>(
            bytes,
            options) ?? throw new InvalidOperationException(
                "The precision census manifest was empty.");
        manifest = manifest with
        {
            Digest = Convert.ToHexString(SHA256.HashData(bytes))
                .ToLowerInvariant()
        };
        manifest.Validate();
        return manifest;
    }

    private void Validate()
    {
        Assert.Equal(1, SchemaVersion);
        Assert.Equal(1_000_000, Retention.MaximumRecords);
        Assert.Equal(0, Retention.StartIndex);
        Assert.Equal("stride-ceiling-eligible-over-cap", Retention.Rule);
        Assert.Equal("case-population-boundary", Retention.RestartScope);
        RequireUnique(ExpectedCases, "expected case");
        RequireUnique(Populations.Select(value => value.Id), "population");
        RequireUnique(FullFrameAssets.Select(value => value.Id), "RAW asset");
        RequireUnique(RawSettings.Select(value => value.Id), "RAW setting");
        RequireUnique(FocusedRois.Select(value => value.Id), "focused ROI");
        RequireUnique(WideColors.Select(value => value.Id), "wide color");
        Assert.Equal(5, FullFrameAssets.Length);
        Assert.Equal(4, RawSettings.Length);
        Assert.Contains(ExcludedAssets, exclusion =>
            exclusion.FileName == "nikon-d70-burst-2.nef" &&
            exclusion.Reason.Contains("byte-identical", StringComparison.Ordinal));
        foreach (var asset in FullFrameAssets)
        {
            Assert.True(File.Exists(Path.Combine(
                GoldenTestPaths.AssetDirectory,
                asset.FileName)), $"Missing census RAW asset {asset.FileName}.");
        }
        foreach (var roi in FocusedRois)
        {
            Assert.Contains(FullFrameAssets, asset => asset.Id == roi.AssetId);
            Assert.True(roi.Left >= 0 && roi.Top >= 0 &&
                roi.Right <= 1 && roi.Bottom <= 1 &&
                roi.Left < roi.Right && roi.Top < roi.Bottom);
        }
        foreach (var color in WideColors)
        {
            Assert.Equal(3, color.LinearSrgb.Length);
            Assert.All(color.LinearSrgb, value => Assert.InRange(value, 0, 1));
        }
    }

    private static void RequireUnique(IEnumerable<string> values, string kind)
    {
        var all = values.ToArray();
        Assert.Equal(all.Length, all.Distinct(StringComparer.Ordinal).Count());
        Assert.All(all, value => Assert.False(
            string.IsNullOrWhiteSpace(value), $"Blank {kind} identifier."));
    }
}

internal sealed record PrecisionRetentionManifest(
    int MaximumRecords,
    int StartIndex,
    string Rule,
    string RestartScope);
internal sealed record PrecisionPopulationManifest(
    string Id,
    string Kind,
    string RowSemantics,
    string Intensity);
internal sealed record PrecisionRawAssetManifest(
    string Id,
    string FileName,
    string Purpose);
internal sealed record PrecisionExclusionManifest(
    string FileName,
    string Reason);
internal sealed record PrecisionRawSettingManifest(
    string Id,
    double? Kelvin,
    double? Tint);
internal sealed record PrecisionRoiManifest(
    string Id,
    string AssetId,
    double Left,
    double Top,
    double Right,
    double Bottom);
internal sealed record PrecisionWideColorManifest(
    string Id,
    double[] LinearSrgb);
internal sealed record PrecisionExposureManifest(
    string Id,
    double ExposureEv,
    int Highlights);
internal sealed record PrecisionStackedManifest(
    double Kelvin,
    double Tint,
    double ExposureEv,
    int Brightness,
    int Contrast,
    int Shadows,
    int Highlights,
    int Saturation,
    int Vibrance,
    int CaptureSharpen,
    int ChromaNr,
    double Rotation,
    double CropLeft,
    double CropTop,
    double CropRight,
    double CropBottom,
    int MaxDimension);

public sealed class PrecisionCensusManifestTests
{
    [Fact]
    public void Manifest_PredeclarationIsValidAndComplete()
    {
        var manifest = PrecisionCensusManifest.Load();

        Assert.Equal(
            [
                "case-5-real-raw",
                "case-2-wide-primaries",
                "case-3-exposure-swings",
                "case-4-stacked-edits",
                "case-1-synthetic-baseline"
            ],
            manifest.ExpectedCases);
        Assert.NotEmpty(manifest.Digest);
        Assert.Equal(119, PrecisionExpectedEvidence.Create(manifest).Count);
    }
}
