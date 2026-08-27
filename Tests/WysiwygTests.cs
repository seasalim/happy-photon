using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(CheckpointCRenderGateCollection.Name)]
public sealed class WysiwygTests
{
    [Fact]
    public void DefaultExportProof_IsByteIdenticalToDevelopPreview()
    {
        const int width = 96;
        const int height = 64;
        var samples = new ushort[width * height * 3];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = (y * width + x) * 3;
            samples[offset] = (ushort)(x * ushort.MaxValue / (width - 1));
            samples[offset + 1] = (ushort)(y * ushort.MaxValue / (height - 1));
            samples[offset + 2] = (ushort)((x + y) * ushort.MaxValue /
                (width + height - 2));
        }

        using var baseImage = RenderPipelineTestSupport.CreateBase(samples, height: height);
        var settings = new EditSettings
        {
            Exposure = 0.35,
            Contrast = 12,
            Saturation = 8
        };
        var pipeline = new RenderPipeline();
        using var preview = pipeline.Render(new RenderRequest(
            baseImage,
            settings,
            RenderIntent.Preview,
            BaseImage.InteractivePreviewMaxDimension,
            new RenderOptions(false, false)));
        using var upstream = pipeline.RenderDisplayRec2020(new RenderRequest(
            baseImage,
            settings,
            RenderIntent.Export,
            null,
            new RenderOptions(false, false)));
        using var proof = RenderFinalizer.FinalizeProof(
            upstream,
            maxDimension: null,
            OutputColorSpace.Srgb,
            OutputSharpeningMode.Off,
            settings.Effects);

        Assert.Equal(
            RenderPipelineTestSupport.ReadPixels(preview.Image),
            RenderPipelineTestSupport.ReadPixels(proof));
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

    [Fact]
    public void ManualGeometry_PreviewAndExportAgreeForIdenticalBase()
    {
        var asset = GoldenTestCases.Assets.Single(
            value => value.Slug == "srgb-reference");
        var renderer = new CurrentPipelineGoldenRenderer();
        using var baseImage = renderer.LoadBase(asset);
        var settings = new HappyPhoton.Models.EditSettings
        {
            HorizonRotation = 3,
            Geometry = new HappyPhoton.Models.GeometrySettings
            {
                Vertical = 35,
                Horizontal = -28,
                Aspect = 22,
                Distortion = -45
            }
        };
        using var preview = renderer.Render(
            baseImage,
            settings,
            RenderIntent.Preview);
        using var export = renderer.Render(
            baseImage,
            settings,
            RenderIntent.Export);

        Assert.Equal(preview.Width, export.Width);
        Assert.Equal(preview.Height, export.Height);
        var comparison = GoldenImageComparer.Compare(
            export,
            preview,
            GoldenComparisonDomain.DisplaySrgb);
        Assert.Equal(0, comparison.MeanDeltaE);
        Assert.Equal(0, comparison.P99DeltaE);
    }

    [Theory]
    [InlineData("canon-eos-350d")]
    [InlineData("srgb-reference")]
    public void ActiveEffects_PreviewAndExportAgreeAtCommonDimension(string slug)
    {
        var asset = GoldenTestCases.Assets.Single(value => value.Slug == slug);
        var renderer = new CurrentPipelineGoldenRenderer();
        using var previewBase = renderer.LoadPreviewBase(asset);
        using var exportBase = renderer.LoadBase(asset);
        var settings = new HappyPhoton.Models.EditSettings
        {
            Contrast = 12,
            Effects = new HappyPhoton.Models.EffectsSettings
            {
                Vignette = -38,
                Midpoint = 64,
                Grain = 27,
                GrainSize = HappyPhoton.Models.GrainSize.Medium
            }
        };
        using var preview = renderer.Render(
            previewBase,
            settings,
            RenderIntent.Preview);
        using var export = renderer.Render(
            exportBase,
            settings,
            RenderIntent.Export);
        var comparison = GoldenImageComparer.Compare(
            export,
            preview,
            GoldenComparisonDomain.DisplaySrgb);

        Assert.True(
            comparison.MeanDeltaE <= 2.0 && comparison.P99DeltaE <= 8.0,
            $"{slug}: active-effects preview/export mean ΔE " +
            $"{comparison.MeanDeltaE:F3}, p99 {comparison.P99DeltaE:F3}.");
    }

    [Fact]
    public void ActiveEffects_SrgbAndDisplayP3AgreeInCommonSpace()
    {
        var asset = GoldenTestCases.Assets.Single(
            value => value.Slug == "display-p3-reference");
        var renderer = new CurrentPipelineGoldenRenderer();
        using var baseImage = renderer.LoadBase(asset);
        var settings = new HappyPhoton.Models.EditSettings
        {
            Effects = new HappyPhoton.Models.EffectsSettings
            {
                Vignette = 24,
                Grain = 19,
                GrainSize = HappyPhoton.Models.GrainSize.Fine
            }
        };
        using var srgb = renderer.Render(
            baseImage,
            settings,
            RenderIntent.Export);
        using var displayP3 = renderer.Render(
            baseImage,
            settings,
            RenderIntent.Export,
            outputColorSpace: HappyPhoton.Models.OutputColorSpace.DisplayP3);
        srgb.SetProfile(ColorProfiles.SRGB);
        displayP3.SetProfile(OutputColorProfiles.Get(
            HappyPhoton.Models.OutputColorSpace.DisplayP3));

        var comparison = GoldenImageComparer.Compare(
            displayP3,
            srgb,
            GoldenComparisonDomain.DisplaySrgb);

        Assert.True(
            comparison.MeanDeltaE <= 1.0 && comparison.P99DeltaE <= 3.0,
            $"Active-effects target agreement mean ΔE " +
            $"{comparison.MeanDeltaE:F3}, p99 {comparison.P99DeltaE:F3}.");
    }
}
