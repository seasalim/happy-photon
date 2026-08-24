using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(CheckpointCRenderGateCollection.Name)]
public sealed class GeometryGoldenTests
{
    public static TheoryData<string, int> Cases => new()
    {
        { "vertical", -100 }, { "vertical", -50 },
        { "vertical", 50 }, { "vertical", 100 },
        { "horizontal", -100 }, { "horizontal", -50 },
        { "horizontal", 50 }, { "horizontal", 100 },
        { "aspect", -100 }, { "aspect", -50 },
        { "aspect", 50 }, { "aspect", 100 },
        { "distortion", -100 }, { "distortion", -50 },
        { "distortion", 50 }, { "distortion", 100 }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void SliderSettingMatchesActiveBaseline(string term, int value)
    {
        var version = GoldenTestPaths.ReadActiveVersion();
        Assert.Equal($"v{RenderPipeline.Version}", version);
        var asset = GoldenTestCases.Assets.Single(item =>
            item.Slug == "srgb-reference");
        var renderer = new CurrentPipelineGoldenRenderer();
        using var baseImage = renderer.LoadBase(asset);
        var geometry = new GeometrySettings();
        switch (term)
        {
            case "vertical": geometry.Vertical = value; break;
            case "horizontal": geometry.Horizontal = value; break;
            case "aspect": geometry.Aspect = value; break;
            case "distortion": geometry.Distortion = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(term));
        }
        using var actual = renderer.Render(
            baseImage,
            new EditSettings { Geometry = geometry });
        var suffix = value < 0 ? $"minus{-value}" : $"plus{value}";
        var path = Path.Combine(
            GoldenTestPaths.GoldenDirectory,
            version,
            $"geometry__{term}-{suffix}.png");
        if (GoldenTestPaths.UpdateGoldens)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            actual.Write(path, ExportEncoder.CreatePngWriteDefines());
            return;
        }

        Assert.True(File.Exists(path), $"Golden is missing: {path}");
        using var expected = new MagickImage(path);
        var comparison = GoldenImageComparer.Compare(
            expected,
            actual,
            GoldenComparisonDomain.DisplaySrgb);
        Assert.True(comparison.MeanDeltaE <= 2 &&
                    (!OperatingSystem.IsLinux() || comparison.P99DeltaE <= 3),
            $"{term} {value}: mean ΔE {comparison.MeanDeltaE:F3}, " +
            $"p99 {comparison.P99DeltaE:F3}.");
    }
}
