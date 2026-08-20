using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WysiwygTests
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
    public void PreviewAndExport_StayWithinVisualBound(
        GoldenAssetCase asset)
    {
        Assert.SkipWhen(
            GoldenTestPaths.ReadActiveVersion() == "pending",
            "awaiting re-baseline");
        if (asset.IsHeic)
        {
            var heic = MagickFormatInfo.Create(MagickFormat.Heic);
            Assert.SkipWhen(
                heic is not { SupportsReading: true },
                "HEIC WYSIWYG skipped because this ImageMagick build " +
                "has no HEIC reader.");
        }

        var renderer = new CurrentPipelineGoldenRenderer();
        using var previewBase = renderer.LoadPreviewBase(asset);
        using var exportBase = renderer.LoadBase(asset);
        // Half-size RAW decoding has its own normative gap; the strict
        // WYSIWYG comparison below isolates the shared render math.
        if (asset.IsRaw)
        {
            using var halfSize = new MagickImage(previewBase.Pixels);
            using var fullSize = new MagickImage(exportBase.Pixels);
            BitmapConversionService.ResizeToMaxDimension(
                fullSize,
                checked((int)Math.Max(
                    halfSize.Width,
                    halfSize.Height)));
            var decodeGap = GoldenImageComparer.Compare(
                fullSize,
                halfSize,
                GoldenComparisonDomain.LinearRec2020);
            Assert.True(
                decodeGap.MeanDeltaE <= 2.8,
                $"{asset.Slug}: half/full RAW decode mean ΔE " +
                $"{decodeGap.MeanDeltaE:F3} (limit 2.8).");
        }

        foreach (var settingsCase in asset.SettingsCases)
        {
            using var preview = renderer.Render(
                previewBase,
                settingsCase.CreateSettings(),
                RenderIntent.Preview);
            using var export = renderer.Render(
                exportBase,
                settingsCase.CreateSettings(),
                RenderIntent.Export);
            AlignForComparison(export, preview);
            var comparison = GoldenImageComparer.Compare(
                export,
                preview,
                GoldenComparisonDomain.DisplaySrgb);

            Assert.True(
                comparison.MeanDeltaE <= 2.0 &&
                comparison.P99DeltaE <= 8.0,
                $"{asset.Slug}__{settingsCase.Slug}: preview/export " +
                $"mean ΔE {comparison.MeanDeltaE:F3} (limit 2.0), " +
                $"p99 ΔE {comparison.P99DeltaE:F3} (limit 8.0).");
        }
    }

    internal static void AlignForComparison(
        MagickImage export,
        MagickImage preview)
    {
        if (export.Width == preview.Width && export.Height == preview.Height)
        {
            return;
        }

        export.Resize(new MagickGeometry(preview.Width, preview.Height)
        {
            IgnoreAspectRatio = true
        });
    }

    [Fact]
    public void PreviewAndDisplayP3Export_AgreeInCommonSpaceForInGamutCase()
    {
        var asset = GoldenTestCases.Assets.Single(
            value => value.Slug == "display-p3-reference");
        var renderer = new CurrentPipelineGoldenRenderer();
        using var baseImage = renderer.LoadBase(asset);
        using var preview = renderer.Render(
            baseImage,
            new HappyPhoton.Models.EditSettings(),
            RenderIntent.Preview);
        using var export = renderer.Render(
            baseImage,
            new HappyPhoton.Models.EditSettings(),
            RenderIntent.Export,
            outputColorSpace: HappyPhoton.Models.OutputColorSpace.DisplayP3);
        preview.SetProfile(ColorProfiles.SRGB);
        export.SetProfile(OutputColorProfiles.Get(
            HappyPhoton.Models.OutputColorSpace.DisplayP3));

        var comparison = GoldenImageComparer.Compare(
            export,
            preview,
            GoldenComparisonDomain.DisplaySrgb);

        Assert.True(
            comparison.MeanDeltaE <= 1.0 && comparison.P99DeltaE <= 3.0,
            $"Display P3 common-space WYSIWYG mean ΔE " +
            $"{comparison.MeanDeltaE:F3}, p99 {comparison.P99DeltaE:F3}.");
    }
}
