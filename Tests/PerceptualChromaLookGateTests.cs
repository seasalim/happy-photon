using System.Net;
using System.Text;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PerceptualChromaLookGateTests
{
    private static readonly GoldenSettingsCase[] LookCases =
    [
        new("saturation-minus-100", () => new EditSettings { Saturation = -100 }),
        new("saturation-minus-50", () => new EditSettings { Saturation = -50 }),
        new("saturation-plus-50", () => new EditSettings { Saturation = 50 }),
        new("saturation-plus-100", () => new EditSettings { Saturation = 100 }),
        new("vibrance-minus-100", () => new EditSettings { Vibrance = -100 }),
        new("vibrance-minus-50", () => new EditSettings { Vibrance = -50 }),
        new("vibrance-plus-50", () => new EditSettings { Vibrance = 50 }),
        new("vibrance-plus-100", () => new EditSettings { Vibrance = 100 }),
        new("combined-minus", () => new EditSettings
        {
            Saturation = -50,
            Vibrance = -50
        }),
        new("combined-plus", () => new EditSettings
        {
            Saturation = 50,
            Vibrance = 50
        })
    ];

    private static readonly int[] ColorCheckerPatches = [0, 1, 6, 8, 11];
    private readonly ITestOutputHelper _output;

    public PerceptualChromaLookGateTests(ITestOutputHelper output) =>
        _output = output;

    [Fact]
    public void FrozenLegacyReference_DiffersFromProductionPerceptualChroma()
    {
        using var legacy = CreateSentinel();
        using var perceptual = CopyWithMaterializedPixels(legacy);
        var settings = new EditSettings { Saturation = 50, Vibrance = 50 };

        ApplyFrozenLegacyModulate(legacy, settings);
        RenderChromaStage.Apply(perceptual, settings);

        Assert.NotEqual(
            RenderPipelineTestSupport.ReadPixels(legacy),
            RenderPipelineTestSupport.ReadPixels(perceptual));
    }

    [Fact]
    public void FreshCopy_MaterializedBeforeBandedChroma_MatchesWholeFrameReference()
    {
        using var upstream = CreateBandSentinel();
        using var reference = new MagickImage(upstream);
        using var banded = CopyWithMaterializedPixels(upstream);
        var settings = new EditSettings { Saturation = 73, Vibrance = 61 };

        RenderChromaStage.Apply(reference, settings, int.MaxValue);
        RenderChromaStage.Apply(
            banded,
            settings,
            checked((int)upstream.Width * 2));

        Assert.Equal(
            RenderPipelineTestSupport.ReadPixels(reference),
            RenderPipelineTestSupport.ReadPixels(banded));
    }

    [Fact]
    public void CanonicalFixtures_GenerateLegacyAndPerceptualHtmlSheet()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_CHROMA_LOOKGATE") != "1",
            "Set HAPPY_PHOTON_CHROMA_LOOKGATE=1 and " +
            "HAPPY_PHOTON_CHROMA_LOOKGATE_DIR to generate the review sheet.");
        var outputDirectory = Environment.GetEnvironmentVariable(
            "HAPPY_PHOTON_CHROMA_LOOKGATE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(outputDirectory),
            "HAPPY_PHOTON_CHROMA_LOOKGATE_DIR must name the run directory.");
        outputDirectory = Path.GetFullPath(outputDirectory);
        var imageDirectory = Directory.CreateDirectory(Path.Combine(
            outputDirectory,
            "images")).FullName;
        var html = StartHtml();
        var differences = 0;
        var renderer = new CurrentPipelineGoldenRenderer();

        foreach (var asset in GoldenTestCases.Assets)
        {
            if (asset.IsHeic && MagickFormatInfo.Create(MagickFormat.Heic) is not
                    { SupportsReading: true })
            {
                html.Append("<h2>").Append(Encode(asset.Slug))
                    .Append("</h2><p>Skipped: ImageMagick has no HEIC reader.</p>");
                continue;
            }
            using var baseImage = renderer.LoadPreviewBase(asset);
            html.Append("<h2>").Append(Encode(asset.Slug))
                .Append("</h2><div class=grid>");
            foreach (var lookCase in LookCases)
            {
                var settings = lookCase.CreateSettings();
                using var upstream = RenderUpstream(baseImage, settings);
                using var legacy = FinishLegacy(upstream, baseImage.Info, settings, 500);
                using var perceptual = FinishPerceptual(
                    upstream, baseImage.Info, settings, 500);
                differences += RenderPipelineTestSupport.ReadPixels(legacy)
                    .AsSpan().SequenceEqual(
                        RenderPipelineTestSupport.ReadPixels(perceptual)) ? 0 : 1;
                var name = $"{asset.Slug}__{lookCase.Slug}.png";
                WritePair(legacy, perceptual, Path.Combine(imageDirectory, name));
                AppendFigure(html, lookCase.Slug, $"images/{name}");
            }
            html.Append("</div>");
        }

        AppendColorChecker(html, imageDirectory);
        html.Append("</body></html>");
        var htmlPath = Path.Combine(outputDirectory, "index.html");
        File.WriteAllText(htmlPath, html.ToString());
        Assert.True(differences > 0,
            "The frozen legacy and production perceptual arms were identical.");
        _output.WriteLine(htmlPath);
    }

    private static void AppendColorChecker(
        StringBuilder html,
        string imageDirectory)
    {
        var manifest = ColorCheckerManifest.Load();
        var oracle = ColorScienceOracleData.Load();
        var fixturePath = Path.Combine(
            GoldenTestPaths.AssetDirectory,
            manifest.Fixture.FileName);
        using var baseImage = new RawBaseLoader().LoadPreviewBase(
            new ImageFile(fixturePath),
            BaseDecodeSettings.Default,
            CancellationToken.None) ?? throw new InvalidOperationException(
                "The ColorChecker look-gate fixture did not decode.");
        var scale = baseImage.Pixels.Width /
            (double)manifest.RenderPath.ExpectedWidth;
        html.Append("<h2>ColorChecker skin protection</h2>")
            .Append("<p>Each crop is legacy left / perceptual right. Reference Lab ")
            .Append("comes from the committed colour-science oracle.</p>");
        foreach (var vibrance in new[] { -100, -50, 50, 100 })
        {
            var settings = new EditSettings
            {
                Vibrance = vibrance,
                Wb = new WhiteBalanceSettings
                {
                    Mode = WbMode.Picked,
                    Gains = manifest.Calibration.MeasuredLinearRec2020Gains
                }
            };
            using var upstream = RenderUpstream(baseImage, settings);
            using var legacy = FinishLegacy(
                upstream, baseImage.Info, settings, maxDimension: null);
            using var perceptual = FinishPerceptual(
                upstream, baseImage.Info, settings, maxDimension: null);
            html.Append("<h3>Vibrance ").Append(vibrance)
                .Append("</h3><div class=patches>");
            foreach (var patchIndex in ColorCheckerPatches)
            {
                var patch = oracle.ColorChecker.Patches[patchIndex];
                var bounds = ColorCheckerSampling.GetPatchBounds(
                    manifest.Geometry,
                    patchIndex,
                    scale);
                using var legacyCrop = Crop(legacy, bounds);
                using var perceptualCrop = Crop(perceptual, bounds);
                var name = $"colorchecker-v{vibrance}-patch-{patchIndex + 1:00}.png";
                WritePair(
                    legacyCrop,
                    perceptualCrop,
                    Path.Combine(imageDirectory, name));
                AppendFigure(
                    html,
                    $"{patchIndex + 1:00} {patch.Name}; " +
                    $"reference Lab {patch.Lab[0]:F1}, {patch.Lab[1]:F1}, " +
                    $"{patch.Lab[2]:F1}",
                    $"images/{name}");
            }
            html.Append("</div>");
        }
    }

    private static MagickImage RenderUpstream(
        BaseImage baseImage,
        EditSettings settings)
    {
        var image = new MagickImage(baseImage.Pixels);
        try
        {
            RenderGeometry.Apply(image, settings);
            var whiteBalance = RenderChromaticStage.CreateWhiteBalanceMatrix(
                baseImage.Info,
                settings);
            if (baseImage.Info.IsRawSource)
            {
                new AgxCrossing(
                    new AgxToneParameters(
                        settings.Exposure,
                        baseImage.Info.SourceExposureBiasEv,
                        settings.Contrast,
                        settings.Highlights,
                        settings.Shadows,
                        settings.Curve,
                        settings.CurveRed,
                        settings.CurveGreen,
                        settings.CurveBlue),
                    whiteBalance,
                    baseImage.Info.DcpProfile?.HueSatMap).Apply(image);
            }
            else
            {
                var normalized = ChromaticAdaptation.NormalizeForRender(whiteBalance);
                ToneLutApplicator.Apply(
                    image,
                    normalized.Matrix,
                    ToneLut.Compose(new ToneParams(
                        settings.Exposure + baseImage.Info.SourceExposureBiasEv,
                        normalized.Fold,
                        settings.Brightness,
                        settings.Contrast,
                        settings.Shadows,
                        settings.Highlights,
                        settings.BaseLook ?? false,
                        settings.Curve,
                        settings.CurveRed,
                        settings.CurveGreen,
                        settings.CurveBlue)));
            }
            RenderColorEncoding.RetagAsSrgb(image);
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static MagickImage FinishLegacy(
        MagickImage upstream,
        BaseImageInfo info,
        EditSettings settings,
        int? maxDimension)
    {
        var image = CopyWithMaterializedPixels(upstream);
        ApplyFrozenLegacyModulate(image, settings);
        return FinishOwned(image, info, settings, maxDimension);
    }

    private static MagickImage FinishPerceptual(
        MagickImage upstream,
        BaseImageInfo info,
        EditSettings settings,
        int? maxDimension)
    {
        var image = CopyWithMaterializedPixels(upstream);
        RenderChromaStage.Apply(image, settings);
        return FinishOwned(image, info, settings, maxDimension);
    }

    private static MagickImage CopyWithMaterializedPixels(MagickImage source)
    {
        var image = new MagickImage(source);
        try
        {
            using var pixels = image.GetPixels();
            var values = pixels.GetArea(0, 0, image.Width, image.Height) ??
                throw new InvalidOperationException(
                    "Unable to materialize look-gate pixels.");
            pixels.SetArea(0, 0, image.Width, image.Height, values);
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static MagickImage FinishOwned(
        MagickImage image,
        BaseImageInfo info,
        EditSettings settings,
        int? maxDimension)
    {
        try
        {
            RenderSharpening.ApplyCapture(image, info, settings.Detail);
            RenderDetail.Apply(image, info, settings.Detail);
            return RenderFinalizer.FinalizeOwned(
                image,
                maxDimension,
                OutputColorSpace.Srgb,
                outputSharpening: false,
                wasResized: false,
                effects: settings.Effects);
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static void ApplyFrozenLegacyModulate(
        MagickImage image,
        EditSettings settings)
    {
        var factor = (100 + settings.Saturation) / 100.0 *
            (100 + settings.Vibrance * 0.5) / 100.0;
        if (factor == 1) return;
        image.Modulate(
            new Percentage(100),
            new Percentage(factor * 100),
            new Percentage(100));
    }

    private static MagickImage Crop(MagickImage source, MagickGeometry bounds)
    {
        var crop = new MagickImage(source);
        crop.Crop(bounds);
        crop.ResetPage();
        crop.Resize(140, 140);
        return crop;
    }

    private static void WritePair(
        MagickImage legacy,
        MagickImage perceptual,
        string path)
    {
        using var images = new MagickImageCollection
        {
            new MagickImage(legacy),
            new MagickImage(perceptual)
        };
        using var pair = images.AppendHorizontally();
        pair.Format = MagickFormat.Png;
        pair.Depth = 16;
        pair.Strip();
        pair.Write(path);
    }

    private static StringBuilder StartHtml() => new StringBuilder("""
        <!doctype html><html><head><meta charset="utf-8"><title>Perceptual chroma look gate</title>
        <style>body{font:14px system-ui;margin:24px;background:#181818;color:#eee}h2{margin-top:36px}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(420px,1fr));gap:18px}.patches{display:flex;flex-wrap:wrap;gap:14px}figure{margin:0;background:#282828;padding:10px}img{display:block;max-width:100%;height:auto}figcaption{margin-top:8px}</style></head><body>
        <h1>Perceptual chroma look gate</h1><p>Every pair uses one shared upstream render: frozen legacy Modulate on the left, production OKLCh on the right.</p>
        """);

    private static void AppendFigure(
        StringBuilder html,
        string caption,
        string relativePath) =>
        html.Append("<figure><img src=\"").Append(Encode(relativePath))
            .Append("\"><figcaption>").Append(Encode(caption))
            .Append(" — legacy left / perceptual right</figcaption></figure>");

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static MagickImage CreateSentinel()
    {
        var image = new MagickImage(MagickColors.Black, 3, 1)
        {
            ColorSpace = ColorSpace.sRGB
        };
        using var pixels = image.GetPixels();
        pixels.SetArea(0, 0, 3, 1,
        [
            9000, 28000, 53000,
            58000, 17000, 7000,
            18000, 46000, 23000
        ]);
        return image;
    }

    private static MagickImage CreateBandSentinel()
    {
        const int width = 5;
        const int height = 11;
        var values = new ushort[width * height * 3];
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            var offset = pixel * 3;
            values[offset] = checked((ushort)(4000 + pixel * 701));
            values[offset + 1] = checked((ushort)(9000 + pixel * 509));
            values[offset + 2] = checked((ushort)(52000 - pixel * 613));
        }

        var image = new MagickImage(MagickColors.Black, width, height)
        {
            ColorSpace = ColorSpace.sRGB
        };
        using var pixels = image.GetPixels();
        pixels.SetArea(0, 0, width, height, values);
        return image;
    }
}
