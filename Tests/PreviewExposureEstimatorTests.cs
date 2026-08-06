using System.Runtime.InteropServices;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PreviewExposureEstimatorTests
{
    [Theory]
    [InlineData(-1.25)]
    [InlineData(0.75)]
    [InlineData(1.25)]
    public void Solve_RecoversKnownExposure(double expectedEv)
    {
        ushort[] baseRgb =
        [
            1200, 2400, 3600,
            7000, 9000, 11000,
            15000, 18000, 21000,
            26000, 29000, 32000,
            39000, 42000, 45000
        ];
        var target = PreviewExposureEstimator.DefaultRenderMedian(
            baseRgb,
            expectedEv);

        var actual = PreviewExposureEstimator.Solve(baseRgb, target);

        Assert.NotNull(actual);
        Assert.InRange(actual.Value, expectedEv - 1e-4, expectedEv + 1e-4);
    }

    [Fact]
    public void Solve_UsesColoredChannelsBeforeLuminance()
    {
        ushort[] coloredRgb =
        [
            62000, 1200, 800,
            900, 1800, 64000,
            50000, 6000, 28000,
            4000, 42000, 9000,
            25000, 14000, 52000
        ];
        const double expectedEv = 1.1;
        var target = PreviewExposureEstimator.DefaultRenderMedian(
            coloredRgb,
            expectedEv);

        var actual = PreviewExposureEstimator.Solve(coloredRgb, target);

        Assert.NotNull(actual);
        Assert.InRange(actual.Value, expectedEv - 1e-4, expectedEv + 1e-4);
    }

    [Fact]
    public void Solve_ClampsTargetsOutsideReachableRange()
    {
        ushort[] baseRgb =
        [
            5000, 9000, 13000,
            17000, 21000, 25000,
            29000, 33000, 37000
        ];
        var low = PreviewExposureEstimator.DefaultRenderMedian(baseRgb, -3);
        var high = PreviewExposureEstimator.DefaultRenderMedian(baseRgb, 3);

        Assert.Equal(-3, PreviewExposureEstimator.Solve(baseRgb, low / 2));
        Assert.Equal(
            3,
            PreviewExposureEstimator.Solve(
                baseRgb,
                Math.Min(1, high + 0.1)));
    }

    [Fact]
    public void Solve_ReturnsFallbackSignalForDegenerateInputs()
    {
        Assert.Null(PreviewExposureEstimator.Solve([0, 0, 0], 0.25));
        Assert.Null(PreviewExposureEstimator.Solve([100, 100, 100], 0));
        Assert.Null(
            PreviewExposureEstimator.Solve(
                [100, 100, 100],
                double.NaN));
    }

    [Theory]
    [InlineData(0.64, 0.58, 0.64)]
    [InlineData(-0.06, 1.72, 1.72)]
    [InlineData(1.33, 0, 1.33)]
    public void SelectBias_RejectsOnlyGrossFujiMetadataDisagreement(
        double previewEstimate,
        double metadataFallback,
        double expected)
    {
        Assert.Equal(
            expected,
            PreviewExposureEstimator.SelectBias(
                previewEstimate,
                metadataFallback));
    }

    [Fact]
    public void SelectBias_UsesFallbackWhenPreviewEstimateIsUnavailable()
    {
        Assert.Equal(
            1.72,
            PreviewExposureEstimator.SelectBias(null, 1.72));
        Assert.Equal(
            1.72,
            PreviewExposureEstimator.SelectBias(double.NaN, 1.72));
    }

    [Fact]
    public void DefaultResponse_MatchesRealRawTonePipeline()
    {
        ushort[] samples =
        [
            5000, 16000, 44000,
            55000, 8000, 24000,
            12000, 38000, 9000,
            32000, 23000, 51000
        ];
        const double exposureEv = 0.8;
        using var baseImage = RenderPipelineTestSupport.CreateBase(
            samples,
            isRaw: true,
            height: 2);
        var request = new RenderRequest(
            baseImage,
            new EditSettings
            {
                Exposure = exposureEv,
                Detail = new DetailSettings { CaptureSharpen = 0 }
            },
            RenderIntent.Export,
            null,
            new RenderOptions(false, false));

        using var rendered = new RenderPipeline().Render(request);
        using var renderedLinear = (MagickImage)rendered.Image.Clone();
        renderedLinear.ColorSpace = ColorSpace.RGB;
        var renderedMedian = PreviewExposureEstimator.MedianLuminance(
            ReadRgb(renderedLinear));
        var estimatedMedian = PreviewExposureEstimator.DefaultRenderMedian(
            samples,
            exposureEv);

        Assert.InRange(
            Math.Abs(renderedMedian - estimatedMedian),
            0,
            2e-4);
    }

    [Fact]
    public void EstimatePrepared_CropsBaseToPreviewAspect()
    {
        const int width = 120;
        const int fullHeight = 80;
        const int croppedHeight = 68;
        const double exposureEv = 0.65;
        var full = CreateScene(width, fullHeight, croppedHeight);
        var cropped = CropRows(full, width, fullHeight, croppedHeight);
        var preview = Transfer(cropped, exposureEv);
        using var fullImage = CreateLinearImage(full, width, fullHeight);
        using var croppedImage = CreateLinearImage(
            cropped,
            width,
            croppedHeight);
        using var previewImage = CreateLinearImage(
            preview,
            width,
            croppedHeight);

        var mismatched = PreviewExposureEstimator.EstimatePrepared(
            fullImage,
            previewImage);
        var matched = PreviewExposureEstimator.EstimatePrepared(
            croppedImage,
            previewImage);

        Assert.NotNull(mismatched);
        Assert.NotNull(matched);
        Assert.InRange(
            mismatched.Value,
            exposureEv - 0.01,
            exposureEv + 0.01);
        Assert.InRange(
            Math.Abs(mismatched.Value - matched.Value),
            0,
            1e-6);
    }

    [Fact]
    public void EstimatePrepared_RejectsSmallPreview()
    {
        using var baseImage = new MagickImage(MagickColors.Gray, 128, 128)
        {
            ColorSpace = ColorSpace.RGB
        };
        using var preview = new MagickImage(MagickColors.Gray, 63, 128)
        {
            ColorSpace = ColorSpace.RGB
        };

        Assert.Null(
            PreviewExposureEstimator.EstimatePrepared(baseImage, preview));
    }

    private static ushort[] CreateScene(
        int width,
        int height,
        int centerHeight)
    {
        var result = new ushort[width * height * 3];
        var firstCenterRow = (height - centerHeight) / 2;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 3;
                var center = y >= firstCenterRow &&
                    y < firstCenterRow + centerHeight;
                result[offset] = center ? (ushort)(8000 + x * 180) : (ushort)500;
                result[offset + 1] = center ? (ushort)(12000 + x * 120) : (ushort)500;
                result[offset + 2] = center ? (ushort)(18000 + x * 80) : (ushort)500;
            }
        }

        return result;
    }

    private static ushort[] CropRows(
        ushort[] source,
        int width,
        int sourceHeight,
        int targetHeight)
    {
        var rowLength = width * 3;
        var result = new ushort[rowLength * targetHeight];
        var firstRow = (sourceHeight - targetHeight) / 2;
        Array.Copy(
            source,
            firstRow * rowLength,
            result,
            0,
            result.Length);
        return result;
    }

    private static ushort[] Transfer(ushort[] source, double exposureEv)
    {
        var result = new ushort[source.Length];
        var gain = Math.Pow(2, exposureEv);
        for (var index = 0; index < source.Length; index++)
        {
            var linear = Math.Min(
                source[index] / (double)ushort.MaxValue * gain,
                1);
            var display = ToneLut.BaseLook(ToneLut.SrgbEncode(linear));
            var decoded = ToneLut.SrgbDecode(display);
            result[index] = (ushort)Math.Round(decoded * ushort.MaxValue);
        }

        return result;
    }

    private static MagickImage CreateLinearImage(
        ushort[] samples,
        int width,
        int height)
    {
        var image = new MagickImage(MagickColors.Black, (uint)width, (uint)height)
        {
            ColorSpace = ColorSpace.RGB,
            Depth = 16
        };
        image.ImportPixels(
            MemoryMarshal.AsBytes(samples.AsSpan()),
            new PixelImportSettings(
                (uint)width,
                (uint)height,
                StorageType.Short,
                PixelMapping.RGB));
        return image;
    }

    private static ushort[] ReadRgb(MagickImage image) =>
        image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGB) ??
        throw new InvalidOperationException("Unable to read RGB pixels.");
}
