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

    [SkippableTheory]
    [MemberData(nameof(AssetMatrix))]
    public void PreviewAndExport_StayWithinVisualBound(
        GoldenAssetCase asset)
    {
        Skip.If(
            GoldenTestPaths.ReadActiveVersion() == "pending",
            "awaiting re-baseline");
        if (asset.IsHeic)
        {
            var heic = MagickFormatInfo.Create(MagickFormat.Heic);
            Skip.If(
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
            var decodeGap = GoldenImageComparer.Compare(fullSize, halfSize);
            Assert.True(
                decodeGap.MeanDeltaE <= 2.8,
                $"{asset.Slug}: half/full RAW decode mean ΔE " +
                $"{decodeGap.MeanDeltaE:F3} (limit 2.8).");
        }

        foreach (var settingsCase in asset.SettingsCases)
        {
            using var preview = renderer.Render(
                asset.IsRaw ? exportBase : previewBase,
                settingsCase.CreateSettings(),
                RenderIntent.Preview);
            using var export = renderer.Render(
                exportBase,
                settingsCase.CreateSettings(),
                RenderIntent.Export);
            var comparison = GoldenImageComparer.Compare(export, preview);

            Assert.True(
                comparison.MeanDeltaE <= 1.5 &&
                comparison.P99DeltaE <= 4.0,
                $"{asset.Slug}__{settingsCase.Slug}: preview/export " +
                $"mean ΔE {comparison.MeanDeltaE:F3} (limit 1.5), " +
                $"p99 ΔE {comparison.P99DeltaE:F3} (limit 4.0).");
        }
    }
}
