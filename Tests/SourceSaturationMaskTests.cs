using System.Runtime.InteropServices;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SourceSaturationMaskTests : IDisposable
{
    private readonly TemporaryDirectory _tempDirectory = new();

    [Fact]
    public void EncodedThreshold_ScalesWithEightAndTenBitMaximum()
    {
        Assert.False(SourceSaturationMask.IsNearEndpoint(252, 255));
        Assert.True(SourceSaturationMask.IsNearEndpoint(253, 255));
        Assert.False(SourceSaturationMask.IsNearEndpoint(1014, 1023));
        Assert.True(SourceSaturationMask.IsNearEndpoint(1015, 1023));
    }

    [Fact]
    public void CaptureEncoded_TenBitMultiWorkerMatchesSerialReference()
    {
        const int width = 17;
        const int height = 128;
        const uint maximum = 1023;
        uint[] boundaryPattern = [0, 1014, 1015, 1023, 512, 1015, 1014];
        var samples = new uint[width * height * 3];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = boundaryPattern[index % boundaryPattern.Length];
        }

        using var image = EncodedImage(maximum, samples, width, height);
        Assert.Equal(10u, image.Depth);
        var expected = FromEncodedSamples(
            width,
            height,
            samples,
            maximum);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var actual = SourceSaturationMask.CaptureEncoded(
                image,
                maximum,
                height,
                CancellationToken.None);

            AssertMasksEqual(expected, actual);
        }
    }

    [Fact]
    public void CaptureEncoded_EightBitGenericPathMatchesSerialBoundaryReference()
    {
        const int width = 17;
        const int height = 128;
        const uint maximum = 255;
        uint[] boundaryPattern = [0, 252, 253, 255, 128, 253, 252];
        var samples = new uint[width * height * 3];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = boundaryPattern[index % boundaryPattern.Length];
        }

        using var image = EncodedImage(maximum, samples, width, height);
        Assert.Equal(8u, image.Depth);
        var expected = FromEncodedSamples(width, height, samples, maximum);

        var actual = SourceSaturationMask.CaptureEncoded(
            image,
            maximum,
            height,
            CancellationToken.None);

        AssertMasksEqual(expected, actual);
    }

    [Fact]
    public void PreviewBase_CapturesJpegBeforeColorNormalization()
    {
        var path = WriteJpeg("source-mask.jpg", 4, 1);
        var loader = new StandardBaseLoader((_, _) => EncodedImage(
            255,
        [
            252, 0, 0,
            253, 0, 0,
            254, 0, 0,
            255, 0, 0
        ]));

        var outcome = loader.LoadPreviewBaseWithOutcome(
            new ImageFile(path),
            BaseDecodeSettings.Default,
            CancellationToken.None);
        using var pair = outcome.Pair;
        var result = pair!.Interactive;
        var sourceSaturation = outcome.Analysis.SourceSaturation;

        Assert.NotNull(sourceSaturation);
        Assert.Equal(0, sourceSaturation!.GetFlags(0, 0));
        Assert.Equal(1, sourceSaturation.GetFlags(1, 0));
        Assert.Equal(1, sourceSaturation.GetFlags(2, 0));
        Assert.Equal(1, sourceSaturation.GetFlags(3, 0));
        Assert.NotEqual(253 * 257, PixelValues(result.Pixels)[3]);
    }

    [Fact]
    public void PreviewBase_UsesReportedTenBitMaximumForHeic()
    {
        var loader = new StandardBaseLoader((_, _) => EncodedImage(
            1023,
        [
            1014, 0, 0,
            1015, 0, 0,
            1023, 0, 0
        ]));

        var outcome = loader.LoadPreviewBaseWithOutcome(
            new ImageFile(Path.Combine(_tempDirectory.Path, "source-mask.heic")),
            BaseDecodeSettings.Default,
            CancellationToken.None);
        using var pair = outcome.Pair;
        var sourceSaturation = outcome.Analysis.SourceSaturation;

        Assert.NotNull(pair);
        Assert.NotNull(sourceSaturation);
        Assert.Equal(0, sourceSaturation!.GetFlags(0, 0));
        Assert.Equal(1, sourceSaturation.GetFlags(1, 0));
        Assert.Equal(1, sourceSaturation.GetFlags(2, 0));
    }

    [Theory]
    [InlineData("source.png")]
    [InlineData("source.tiff")]
    public void PreviewBase_OtherStandardFormatsHaveNoSourceArtifact(string name)
    {
        var loader = new StandardBaseLoader((_, _) => EncodedImage(
            255,
            [255, 255, 255]));

        var outcome = loader.LoadPreviewBaseWithOutcome(
            new ImageFile(Path.Combine(_tempDirectory.Path, name)),
            BaseDecodeSettings.Default,
            CancellationToken.None);
        using var pair = outcome.Pair;

        Assert.NotNull(pair);
        Assert.Null(outcome.Analysis.SourceSaturation);
    }

    private string WriteJpeg(string name, int width, int height)
    {
        var path = Path.Combine(_tempDirectory.Path, name);
        using var image = new MagickImage(
            MagickColors.Black,
            checked((uint)width),
            checked((uint)height));
        image.Format = MagickFormat.Jpeg;
        image.Write(path);
        return path;
    }

    private static MagickImage EncodedImage(
        uint maximum,
        IReadOnlyList<uint> encoded) =>
        EncodedImage(maximum, encoded, encoded.Count / 3, 1);

    private static MagickImage EncodedImage(
        uint maximum,
        IReadOnlyList<uint> encoded,
        int width,
        int height)
    {
        Assert.Equal(checked(width * height * 3), encoded.Count);
        var samples = encoded.Select(value => checked((ushort)Math.Round(
            value * (double)ushort.MaxValue / maximum))).ToArray();
        var settings = new PixelReadSettings(
            checked((uint)width),
            checked((uint)height),
            StorageType.Short,
            PixelMapping.RGB);
        settings.ReadSettings.ColorSpace = ColorSpace.sRGB;
        return new MagickImage(
            MemoryMarshal.AsBytes(samples.AsSpan()),
            settings)
        {
            Depth = checked((uint)Math.Round(Math.Log2(maximum + 1)))
        };
    }

    private static SourceSaturationMask FromEncodedSamples(
        int width,
        int height,
        IReadOnlyList<uint> samples,
        uint encodedMaximum)
    {
        Assert.Equal(checked(width * height * 3), samples.Count);
        var result = new SourceSaturationMask(width, height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = (y * width + x) * 3;
            byte flags = 0;
            if (SourceSaturationMask.IsNearEndpoint(
                    samples[offset], encodedMaximum)) flags |= 1;
            if (SourceSaturationMask.IsNearEndpoint(
                    samples[offset + 1], encodedMaximum)) flags |= 2;
            if (SourceSaturationMask.IsNearEndpoint(
                    samples[offset + 2], encodedMaximum)) flags |= 4;
            result.SetFlags(x, y, flags);
        }
        return result;
    }

    private static void AssertMasksEqual(
        SourceSaturationMask expected,
        SourceSaturationMask actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                Assert.Equal(expected.GetFlags(x, y), actual.GetFlags(x, y));
            }
        }
    }

    private static ushort[] PixelValues(MagickImage image)
    {
        using var pixels = image.GetPixels();
        return pixels.ToShortArray(PixelMapping.RGB) ??
            throw new InvalidOperationException("Could not read image pixels.");
    }

    public void Dispose() => _tempDirectory.Dispose();
}
