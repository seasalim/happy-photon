using ImageMagick;
using HappyPhoton.LibRaw.Interop;
using System.Runtime.InteropServices;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PipelineTestAssetTests
{
    [Fact]
    public void BurstPair_IsByteIdentical()
    {
        var first = File.ReadAllBytes(GoldenTestPaths.Asset("nikon-d70-burst-1.nef"));
        var second = File.ReadAllBytes(GoldenTestPaths.Asset("nikon-d70-burst-2.nef"));

        Assert.Equal(first, second);
    }

    [Fact]
    public void MetadataReference_HasGpsAndOrientationSix()
    {
        using var image = new MagickImage(GoldenTestPaths.Asset("srgb-exif-gps-orientation-6.jpg"));
        var exif = image.GetExifProfile();

        Assert.NotNull(exif);
        Assert.Equal(OrientationType.RightTop, image.Orientation);
        Assert.Equal((ushort)OrientationType.RightTop,
            exif!.GetValue(ExifTag.Orientation)?.Value);
        Assert.NotNull(exif.GetValue(ExifTag.GPSLatitude));
        Assert.NotNull(exif.GetValue(ExifTag.GPSLongitude));
    }

    [Theory]
    [InlineData("srgb-reference.jpg")]
    [InlineData("display-p3-reference.jpg")]
    [InlineData("adobe-rgb-reference.jpg")]
    public void ColorReference_HasEmbeddedProfile(string fileName)
    {
        using var image = new MagickImage(GoldenTestPaths.Asset(fileName));
        var profile = image.GetColorProfile();

        Assert.NotNull(profile);
        Assert.NotEmpty(profile!.ToByteArray());
    }

    [Fact]
    public void TiffReference_IsSixteenBit()
    {
        using var image = new MagickImage(GoldenTestPaths.Asset("reference-16bit.tiff"));

        Assert.Equal(16u, image.Depth);
    }

    [Fact]
    public void FbddReference_IsCanon6dAtIso6400()
    {
        using var context = LibRawContext.Open(
            GoldenTestPaths.Asset("canon-eos-6d-iso-6400.cr2"));
        var metadata = context.GetMetadata();

        Assert.Equal("Canon", metadata.Make?.Trim());
        Assert.Equal("EOS 6D", metadata.Model?.Trim());
        Assert.Equal(6400, metadata.Iso);
    }

    [Fact]
    public void SyntheticGradient_CoversUnitRangeMonotonically()
    {
        using var gradient = PipelineTestImages.CreateUnitGradient();
        using var pixels = gradient.GetPixels();
        var values = pixels.ToShortArray(PixelMapping.RGB);
        Assert.NotNull(values);
        Assert.Equal(0, values![0]);
        Assert.Equal(ushort.MaxValue, values[^3]);

        for (var index = 3; index < values.Length; index += 3)
        {
            Assert.True(values[index] >= values[index - 3],
                $"Gradient folded at sample {index / 3}.");
        }
    }

    [Fact]
    public void ReferenceRaw_HasClippedHighlights()
    {
        using var context = LibRawContext.Open(GoldenTestPaths.Asset("canon-eos-350d.cr2"));
        context.Unpack();
        context.ConfigureOutput(new LibRawOutputConfiguration
        {
            AbiVersion = LibRawOutputConfiguration.Version, OutputBits = 16, OutputColor = 1,
            GammaPower = 1, GammaSlope = 1, NoAutoBright = true,
            UseCameraWhiteBalance = true, UseCameraMatrix = true
        });
        context.Process();
        using var processed = context.MakeProcessedImage();
        Assert.Equal(16u, processed.Description.BitsPerSample);
        var samples = MemoryMarshal.Cast<byte, ushort>(processed.AsSpan());
        var clippedSamples = 0;
        foreach (var sample in samples)
        {
            if (sample == ushort.MaxValue)
            {
                clippedSamples++;
            }
        }

        Assert.True(clippedSamples > 0,
            "Reference CR2 must clip in a no-auto-bright, linear 16-bit decode.");
    }
}
