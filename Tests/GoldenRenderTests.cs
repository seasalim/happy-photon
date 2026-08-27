using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(CheckpointCRenderGateCollection.Name)]
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

    [Theory]
    [MemberData(nameof(AssetMatrix))]
    public void CurrentPipeline_MatchesBaselineAndPreviewExportBound(
        GoldenAssetCase asset)
    {
        var activeVersion = GoldenTestPaths.ReadActiveVersion();
        Assert.SkipWhen(activeVersion == "pending", "awaiting re-baseline");
        Assert.Equal($"v{RenderPipeline.Version}", activeVersion);
        if (asset.IsHeic)
        {
            var heic = MagickFormatInfo.Create(MagickFormat.Heic);
            Assert.SkipWhen(heic is not { SupportsReading: true },
                "HEIC golden skipped because this ImageMagick build has no HEIC reader.");
        }

        var renderer = new CurrentPipelineGoldenRenderer();
        using var previewBase = renderer.LoadPreviewBase(asset);
        using var exportBase = renderer.LoadBase(asset);
        AssertRawDecodeGap(asset, previewBase, exportBase);
        foreach (var settingsCase in asset.SettingsCases)
        {
            var settings = settingsCase.CreateSettings();
            using var export = renderer.Render(exportBase, settings);
            var fileName = $"{asset.Slug}__{settingsCase.Slug}.png";
            var baselinePath = Path.Combine(
                GoldenTestPaths.GoldenDirectory, activeVersion, fileName);

            if (GoldenTestPaths.UpdateGoldens)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
                export.Write(baselinePath, ExportEncoder.CreatePngWriteDefines());
            }
            else
            {
                AssertGolden(export, baselinePath, fileName, activeVersion);
            }

            using var preview = renderer.Render(
                previewBase,
                settings,
                RenderIntent.Preview);
            WysiwygTests.AlignForComparison(export, preview);
            var wysiwyg = GoldenImageComparer.Compare(
                export,
                preview,
                GoldenComparisonDomain.DisplaySrgb);
            Assert.True(
                wysiwyg.MeanDeltaE <= 2.0 && wysiwyg.P99DeltaE <= 8.0,
                $"{fileName}: preview/export mean ΔE " +
                $"{wysiwyg.MeanDeltaE:F3} (limit 2.0), p99 ΔE " +
                $"{wysiwyg.P99DeltaE:F3} (limit 8.0).");
        }
    }

    private static void AssertRawDecodeGap(
        GoldenAssetCase asset,
        BaseImage previewBase,
        BaseImage exportBase)
    {
        if (!asset.IsRaw) return;

        using var preview = new MagickImage(previewBase.Pixels);
        using var export = new MagickImage(exportBase.Pixels);
        BitmapConversionService.ResizeToMaxDimension(
            export,
            checked((int)Math.Max(preview.Width, preview.Height)));
        var comparison = GoldenImageComparer.Compare(
            export,
            preview,
            GoldenComparisonDomain.LinearRec2020);
        Assert.True(
            comparison.MeanDeltaE <= 2.8,
            $"{asset.Slug}: half/full RAW decode mean ΔE " +
            $"{comparison.MeanDeltaE:F3} (limit 2.8).");
    }

    private static void AssertGolden(
        MagickImage actual,
        string baselinePath,
        string fileName,
        string activeVersion)
    {
        Assert.True(File.Exists(baselinePath),
            $"Golden is missing: {baselinePath}. Regenerate with " +
            "HAPPY_PHOTON_UPDATE_GOLDENS=1 dotnet test.");

        using var expected = new MagickImage(baselinePath);
        var comparison = GoldenImageComparer.Compare(
            expected,
            actual,
            GoldenComparisonDomain.DisplaySrgb);
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
