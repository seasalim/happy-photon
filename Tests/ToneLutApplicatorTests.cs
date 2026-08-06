using System.Runtime.InteropServices;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ToneLutApplicatorTests
{
    [Theory]
    [InlineData(0.0, 0, 0, 0)]
    [InlineData(1.25, 15, 30, -50)]
    [InlineData(-1.5, -20, -40, 80)]
    public void Apply_IsBitIdenticalToPinnedMagickClut(
        double exposure,
        int brightness,
        int contrast,
        int highlights)
    {
        var lut = ToneLut.Compose(new ToneParams(
            exposure,
            1,
            brightness,
            contrast,
            20,
            highlights,
            false,
            new CurveData()));
        using var expected = CreateAllQuantumValues();
        using var actual = (MagickImage)expected.Clone();

        RenderColorEncoding.ApplyLut(expected, lut);
        ToneLutApplicator.Apply(actual, lut);

        Assert.Equal(
            RenderPipelineTestSupport.ReadPixels(expected),
            RenderPipelineTestSupport.ReadPixels(actual));
    }

    [Fact]
    public void Apply_PreservesAlpha()
    {
        ushort[] samples = [1000, 2000, 3000, 12345];
        using var image = new MagickImage(MagickColors.Transparent, 1, 1);
        image.ImportPixels(
            MemoryMarshal.AsBytes(samples.AsSpan()),
            new PixelImportSettings(
                1,
                1,
                StorageType.Short,
                PixelMapping.RGBA));

        ToneLutApplicator.Apply(
            image,
            ToneLut.Compose(new ToneParams(
                1,
                1,
                0,
                0,
                0,
                0,
                false,
                new CurveData())));

        var actual = image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGBA) ??
            throw new InvalidOperationException("Unable to read RGBA pixels.");
        Assert.Equal(samples[3], actual[3]);
    }

    private static MagickImage CreateAllQuantumValues()
    {
        var samples = new ushort[(ushort.MaxValue + 1) * 3];
        for (var sample = 0; sample <= ushort.MaxValue; sample++)
        {
            var offset = sample * 3;
            samples[offset] = (ushort)sample;
            samples[offset + 1] = (ushort)(ushort.MaxValue - sample);
            samples[offset + 2] = (ushort)sample;
        }

        return RawBaseLoader.ImportRgb16(
            MemoryMarshal.AsBytes(samples.AsSpan()),
            ushort.MaxValue + 1,
            1);
    }
}
