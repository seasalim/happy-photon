using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class GoldenRenderTests
{
    public static TheoryData<GoldenAssetCase> AssetMatrix
    {
        get
        {
            var data = new TheoryData<GoldenAssetCase>();
            foreach (var asset in GoldenTestCases.Assets)
            {
                data.Add(asset);
            }

            return data;
        }
    }

    [SkippableTheory]
    [MemberData(nameof(AssetMatrix))]
    public void CurrentPipeline_MatchesActiveBaseline(GoldenAssetCase asset)
    {
        var activeVersion = GoldenTestPaths.ReadActiveVersion();
        Skip.If(activeVersion == "pending", "awaiting re-baseline");
        Assert.Equal($"v{RenderPipeline.Version}", activeVersion);
        if (asset.IsHeic)
        {
            var heic = MagickFormatInfo.Create(MagickFormat.Heic);
            Skip.If(heic is not { SupportsReading: true },
                "HEIC golden skipped because this ImageMagick build has no HEIC reader.");
        }

        var renderer = new CurrentPipelineGoldenRenderer();
        using var baseImage = renderer.LoadBase(asset);
        foreach (var settingsCase in asset.SettingsCases)
        {
            using var actual = renderer.Render(baseImage, settingsCase.CreateSettings());
            var fileName = $"{asset.Slug}__{settingsCase.Slug}.png";
            var baselinePath = Path.Combine(
                GoldenTestPaths.GoldenDirectory, activeVersion, fileName);

            if (GoldenTestPaths.UpdateGoldens)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
                actual.Write(baselinePath);
                continue;
            }

            Assert.True(File.Exists(baselinePath),
                $"Golden is missing: {baselinePath}. Regenerate with " +
                "HAPPY_PHOTON_UPDATE_GOLDENS=1 dotnet test.");

            using var expected = new MagickImage(baselinePath);
            var comparison = GoldenImageComparer.Compare(expected, actual);
            var crossPlatform = !OperatingSystem.IsLinux();
            var meanLimit = crossPlatform ? 2.0 : 1.0;
            Assert.True(
                comparison.MeanDeltaE <= meanLimit &&
                (crossPlatform || comparison.P99DeltaE <= 3.0),
                $"{fileName} differs from {activeVersion}: " +
                $"mean ΔE {comparison.MeanDeltaE:F3} (limit {meanLimit:F1}), " +
                $"p99 ΔE {comparison.P99DeltaE:F3}" +
                (crossPlatform ? "." : " (limit 3.0)."));
        }
    }
}
