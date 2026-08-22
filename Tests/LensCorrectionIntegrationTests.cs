using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LensCorrectionIntegrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"happy-photon-raw-optics-{Guid.NewGuid():N}");

    [Fact]
    public void DefaultCropMapsFromLibRawActiveAreaOutput()
    {
        var path = SyntheticRawDngFactory.Write(
            _directory,
            new SyntheticRawDngOptions { WarpKr1 = 0, VignetteK0 = 0 });
        var loader = new RawBaseLoader();
        var file = new ImageFile(path);
        var inactive = new BaseDecodeSettings(
            HlReconstructionMode.Clip,
            FbddMode.Off,
            Distortion: false,
            ChromaticAberration: false,
            Vignetting: false);
        var active = inactive with { Distortion = true };

        using var uncorrected = loader.LoadPreviewBase(
            file, inactive, CancellationToken.None);
        using var corrected = loader.LoadPreviewBase(
            file, active, CancellationToken.None);

        Assert.NotNull(uncorrected);
        Assert.NotNull(corrected);
        Assert.Equal((304u, 224u),
            (uncorrected.Pixels.Width, uncorrected.Pixels.Height));
        Assert.Equal((272u, 200u),
            (corrected.Pixels.Width, corrected.Pixels.Height));
    }

    [Fact]
    public void InactivePrescriptionIsBitIdenticalToOpcodeFreeDecode()
    {
        var options = new SyntheticRawDngOptions();
        var prescribedPath = SyntheticRawDngFactory.Write(_directory, options);
        var plainPath = SyntheticRawDngFactory.Write(
            _directory, options with { IncludeOpcodes = false });
        var inactive = InactiveSettings();
        var loader = new RawBaseLoader();

        using var prescribed = loader.LoadPreviewBase(
            new ImageFile(prescribedPath), inactive, CancellationToken.None);
        using var plain = loader.LoadPreviewBase(
            new ImageFile(plainPath), inactive, CancellationToken.None);

        Assert.NotNull(prescribed);
        Assert.NotNull(plain);
        Assert.Equal(
            RawBaseLoaderTestSupport.PixelHash(plain.Pixels),
            RawBaseLoaderTestSupport.PixelHash(prescribed.Pixels));
    }

    [Fact]
    public void CorrectedPreviewRestoresAuthoredRadialCoordinateField()
    {
        var path = SyntheticRawDngFactory.Write(
            _directory,
            new SyntheticRawDngOptions
            {
                Scale = 6,
                WarpKr1 = -0.08,
                VignetteK0 = 0.05
            });
        var loader = new RawBaseLoader();
        var file = new ImageFile(path);
        var inactive = InactiveSettings();
        var active = inactive with { Distortion = true, Vignetting = true };

        using var uncorrected = loader.LoadPreviewBase(
            file, inactive, CancellationToken.None);
        using var corrected = loader.LoadPreviewBase(
            file, active, CancellationToken.None);

        Assert.NotNull(uncorrected);
        Assert.NotNull(corrected);
        var uncorrectedResidual = LinearResidualPixels(uncorrected.Pixels);
        var correctedResidual = LinearResidualPixels(corrected.Pixels);
        // The direct 1600 px inversion oracle pins <= 0.25 px. This loader-level
        // bound also includes LibRaw's half-size Bayer demosaic and integer quantization.
        Assert.True(correctedResidual <= 0.5,
            $"Corrected residual was {correctedResidual:F3} px; " +
            $"uncorrected was {uncorrectedResidual:F3} px.");
        Assert.True(correctedResidual < uncorrectedResidual * 0.25,
            $"Correction did not materially improve the radial field: " +
            $"{uncorrectedResidual:F3} -> {correctedResidual:F3} px.");
    }

    [Fact]
    public void CorrectedPreviewKeepsSaturationMaskOnWarpedHighlight()
    {
        var path = SyntheticRawDngFactory.Write(
            _directory,
            new SyntheticRawDngOptions
            {
                WarpKr1 = -0.08,
                VignetteK0 = 0,
                SaturatedRedSite = (500, 240)
            });
        var settings = InactiveSettings() with { Distortion = true };
        var outcome = new RawBaseLoader().LoadPreviewBaseWithOutcome(
            new ImageFile(path), settings, CancellationToken.None);
        using var pair = outcome.Pair;

        Assert.NotNull(pair);
        var mask = Assert.IsType<SourceSaturationMask>(outcome.Analysis.SourceSaturation);
        var maskPoints = Enumerable.Range(0, mask.Height)
            .SelectMany(y => Enumerable.Range(0, mask.Width)
                .Where(x => (mask.GetFlags(x, y) & 1) != 0)
                .Select(x => (X: x, Y: y)))
            .ToArray();
        Assert.NotEmpty(maskPoints);
        var maskCenter = (
            X: maskPoints.Average(point => point.X),
            Y: maskPoints.Average(point => point.Y));
        var highlight = FindStrongestRedPixel(pair.Interactive.Pixels);
        var distance = Math.Sqrt(
            Math.Pow(maskCenter.X - highlight.X, 2) +
            Math.Pow(maskCenter.Y - highlight.Y, 2));

        Assert.True(distance <= 4,
            $"Warped red mask center ({maskCenter.X:F1},{maskCenter.Y:F1}) " +
            $"missed highlight ({highlight.X},{highlight.Y}) by {distance:F2} px.");
    }

    [Fact]
    public void CorrectedBaseComposesWithRotationHorizonAndCropWithoutBlankPixels()
    {
        var path = SyntheticRawDngFactory.Write(_directory, new SyntheticRawDngOptions());
        var settings = InactiveSettings() with { Distortion = true, Vignetting = true };
        using var corrected = new RawBaseLoader().LoadPreviewBase(
            new ImageFile(path), settings, CancellationToken.None);
        Assert.NotNull(corrected);
        using var rendered = new MagickImage(corrected.Pixels);

        RenderGeometry.Apply(rendered, new EditSettings
        {
            Rotation = 90,
            HorizonRotation = 7,
            Crop = new CropRegion
            {
                Left = 0.12,
                Top = 0.10,
                Right = 0.88,
                Bottom = 0.90
            }
        });

        Assert.Equal(0, CountBlackPixels(rendered));
    }

    [Fact]
    public void OrientationSixPrescriptionDoesNotRotatePreOrientedLibRawBufferTwice()
    {
        var path = SyntheticRawDngFactory.Write(
            _directory,
            new SyntheticRawDngOptions
            {
                Orientation = 6,
                WarpKr1 = 0,
                VignetteK0 = 0
            });
        var settings = InactiveSettings() with { Distortion = true };

        using var corrected = new RawBaseLoader().LoadPreviewBase(
            new ImageFile(path), settings, CancellationToken.None);

        Assert.NotNull(corrected);
        Assert.Equal((200u, 271u),
            (corrected.Pixels.Width, corrected.Pixels.Height));
        Assert.Equal((400, 544),
            (corrected.Info.FullWidth, corrected.Info.FullHeight));
    }

    [Fact]
    public void UncoverablePrescriptionFallsBackToUncorrectedDecode()
    {
        var path = SyntheticRawDngFactory.Write(
            _directory,
            new SyntheticRawDngOptions { WarpKr1 = 1000, VignetteK0 = 0 });
        var settings = InactiveSettings() with { Distortion = true };

        using var decoded = new RawBaseLoader().LoadPreviewBase(
            new ImageFile(path), settings, CancellationToken.None);

        Assert.NotNull(decoded);
        Assert.Equal((304u, 224u), (decoded.Pixels.Width, decoded.Pixels.Height));
        Assert.Null(decoded.Info.LensPrescriptionSummary);
    }

    private static BaseDecodeSettings InactiveSettings() => new(
        HlReconstructionMode.Clip,
        FbddMode.Off,
        Distortion: false,
        ChromaticAberration: false,
        Vignetting: false);

    private static double LinearResidualPixels(MagickImage image)
    {
        using var pixels = image.GetPixelsUnsafe();
        var values = pixels.ToShortArray(PixelMapping.RGB)!;
        var width = checked((int)image.Width);
        var y = checked((int)image.Height / 2);
        var first = values[(y * width + 4) * 3 + 1];
        var last = values[(y * width + width - 5) * 3 + 1];
        var slope = (last - first) / (double)(width - 9);
        var maximum = 0.0;
        for (var x = 4; x < width - 4; x++)
        {
            var expected = first + (x - 4) * slope;
            var actual = values[(y * width + x) * 3 + 1];
            maximum = Math.Max(maximum, Math.Abs(actual - expected) / Math.Abs(slope));
        }
        return maximum;
    }

    private static (int X, int Y) FindStrongestRedPixel(MagickImage image)
    {
        using var pixels = image.GetPixelsUnsafe();
        var values = pixels.ToShortArray(PixelMapping.RGB)!;
        var width = checked((int)image.Width);
        var height = checked((int)image.Height);
        var bestScore = double.NegativeInfinity;
        var best = (X: 0, Y: 0);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = (y * width + x) * 3;
            var score = values[offset] -
                (values[offset + 1] + values[offset + 2]) * 0.5;
            if (score <= bestScore) continue;
            bestScore = score;
            best = (x, y);
        }
        return best;
    }

    private static int CountBlackPixels(MagickImage image)
    {
        using var pixels = image.GetPixelsUnsafe();
        var values = pixels.ToShortArray(PixelMapping.RGB)!;
        var black = 0;
        for (var offset = 0; offset < values.Length; offset += 3)
        {
            if (values[offset] == 0 && values[offset + 1] == 0 &&
                values[offset + 2] == 0) black++;
        }
        return black;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
