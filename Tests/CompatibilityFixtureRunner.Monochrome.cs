using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;

namespace HappyPhoton.Tests;

internal static partial class CompatibilityFixtureRunner
{
    private static void ObserveCameraColor(
        CompatibilityObservation observation,
        BaseImage? fullBase)
    {
        if (fullBase?.Info.IsMonochrome == true)
        {
            ObserveMonochromeColor(observation, fullBase);
            return;
        }
        if (observation.CamMul == null || observation.CamToSrgb == null ||
            fullBase?.Info.CamMul == null || fullBase.Info.CamToSrgb == null)
        {
            observation.Capabilities["cameraColor"] =
                observation.UnpackError != null ? "unsupported" : "degraded";
            return;
        }

        try
        {
            var baseMatrix = Flatten(fullBase.Info.CamToSrgb);
            Require(
                observation.CamMul.Length is 3 or 4 &&
                observation.MatrixRows == 3 &&
                observation.MatrixColumns == observation.CamMul.Length &&
                fullBase.Info.CamMul.Length == observation.CamMul.Length &&
                baseMatrix.Length == observation.CamToSrgb.Length,
                "Native camera-fact dimensions were invalid.");
            for (var index = 0; index < observation.CamMul.Length; index++)
            {
                Require(
                    Math.Abs(fullBase.Info.CamMul[index] - observation.CamMul[index]) <= 1e-6,
                    $"Application CamMul differed at index {index}.");
            }
            for (var index = 0; index < baseMatrix.Length; index++)
            {
                Require(
                    Math.Abs(baseMatrix[index] - observation.CamToSrgb[index]) <= 1e-6,
                    $"Application CamToSrgb differed at index {index}.");
            }
            for (var row = 0; row < observation.MatrixRows; row++)
            {
                var sum = observation.CamToSrgb
                    .Skip(row * observation.MatrixColumns)
                    .Take(observation.MatrixColumns)
                    .Sum();
                Require(
                    Math.Abs(sum - 1) <= 1e-5,
                    $"Camera-to-sRGB row {row} summed to {sum:R}, not 1.");
            }
            observation.Capabilities["cameraColor"] = "pass";
        }
        catch (Exception exception)
        {
            RecordFailure(observation, "cameraColor", exception);
        }
    }

    private static void ObserveMonochromeColor(
        CompatibilityObservation observation,
        BaseImage fullBase)
    {
        try
        {
            Require(
                observation.Sensor?.Colors == 1,
                "Monochrome base did not originate from a colors=1 sensor.");
            AssertMonochromeBase(fullBase);
            observation.Capabilities["cameraColor"] = "pass";
        }
        catch (Exception exception)
        {
            RecordFailure(observation, "cameraColor", exception);
        }
    }

    private static void AssertMonochromeBase(BaseImage image)
    {
        Require(image.Info.CamMul == null, "Mono base exposed camera multipliers.");
        Require(image.Info.CamToSrgb == null, "Mono base exposed a camera matrix.");
        Require(image.Info.DcpProfile == null, "Mono base exposed a DCP payload.");
        Require(image.Info.ProfileToken.Length == 0, "Mono base exposed a profile token.");
        Require(
            image.Info.ProfileStatus == DcpProfileErrorCode.None &&
            image.Info.ProfileMessage == null && image.Info.CameraIdentity == null,
            "Mono base exposed profile characterization facts.");
        RequireNeutral(image.Pixels, "decoded monochrome base");
    }

    private static void ObserveMonochromeRender(
        BaseImage fullBase,
        CompatibilityFixture fixture,
        string resultsDirectory,
        bool saveReviewImage)
    {
        var pipeline = new RenderPipeline();
        var baselineSettings = MonochromeToneSettings();
        var extremeSettings = ExtremeMonochromeSettings();
        using var baseline = pipeline.Render(MonochromeRequest(
            fullBase,
            baselineSettings,
            RenderIntent.Preview,
            OutputColorSpace.Srgb));
        using var preview = pipeline.Render(MonochromeRequest(
            fullBase,
            extremeSettings,
            RenderIntent.Preview,
            OutputColorSpace.Srgb));
        using var srgb = pipeline.Render(MonochromeRequest(
            fullBase,
            extremeSettings,
            RenderIntent.Export,
            OutputColorSpace.Srgb));
        using var displayP3 = pipeline.Render(MonochromeRequest(
            fullBase,
            extremeSettings,
            RenderIntent.Export,
            OutputColorSpace.DisplayP3));

        Require(
            ReadRgb(baseline.Image).SequenceEqual(ReadRgb(preview.Image)),
            "Mono color-only settings changed preview pixels.");
        RequireNeutral(preview.Image, "monochrome preview");
        RequireNeutral(srgb.Image, "monochrome sRGB render");
        RequireNeutral(displayP3.Image, "monochrome Display P3 render");

        if (saveReviewImage)
        {
            Directory.CreateDirectory(resultsDirectory);
            using var review = new MagickImage(srgb.Image);
            review.Format = MagickFormat.Jpeg;
            review.Quality = 90;
            review.Write(Path.Combine(
                resultsDirectory,
                $"{fixture.Slug}-default.jpg"));
        }
    }

    private static EditSettings MonochromeToneSettings() => new()
    {
        Exposure = 0.75,
        Contrast = 35,
        Highlights = -40,
        Shadows = 25,
        Curve = Curve(0.45, 0.58)
    };

    private static EditSettings ExtremeMonochromeSettings()
    {
        var settings = MonochromeToneSettings();
        settings.Wb = new WhiteBalanceSettings
        {
            Mode = WbMode.Custom,
            Kelvin = 12000,
            Tint = 100
        };
        settings.RawProfile = new RawProfileSelection
        {
            Source = RawProfileSource.Embedded,
            ContentHash = new string('f', 64)
        };
        settings.Saturation = 100;
        settings.Vibrance = 100;
        settings.Mixer = ExtremeMixer();
        settings.CurveRed = Curve(0.35, 0.95);
        settings.CurveGreen = Curve(0.5, 0.05);
        settings.CurveBlue = Curve(0.7, 0.9);
        return settings;
    }

    private static CurveData Curve(double x, double y)
    {
        var curve = new CurveData();
        curve.AddPointAndReturnIndex(x, y);
        return curve;
    }

    private static ColorMixerSettings ExtremeMixer()
    {
        var mixer = new ColorMixerSettings();
        foreach (var band in Enum.GetValues<ColorMixerBand>())
        {
            var settings = mixer.GetBand(band);
            settings.Hue = 100;
            settings.Saturation = -100;
            settings.Luminance = 100;
        }
        return mixer;
    }

    private static RenderRequest MonochromeRequest(
        BaseImage image,
        EditSettings settings,
        RenderIntent intent,
        OutputColorSpace outputColorSpace) => new(
            image,
            settings,
            intent,
            500,
            new RenderOptions(false, false),
            outputColorSpace);

    private static ushort[] ReadRgb(MagickImage image) =>
        image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGB) ??
        throw new InvalidOperationException("Unable to read RGB pixels.");

    private static void RequireNeutral(MagickImage image, string stage)
    {
        var values = ReadRgb(image);
        for (var offset = 0; offset < values.Length; offset += 3)
        {
            Require(
                values[offset] == values[offset + 1] &&
                values[offset] == values[offset + 2],
                $"{stage} diverged at pixel {offset / 3}.");
        }
    }
}
