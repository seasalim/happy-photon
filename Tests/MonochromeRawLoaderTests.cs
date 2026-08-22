using System.Runtime.InteropServices;
using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class MonochromeRawLoaderTests
{
    [Fact]
    public void CompatibilityManifest_PinsReviewedMonochromeFixture()
    {
        var manifest = CompatibilityFixtureManifest.Load(Path.Combine(
            GoldenTestPaths.RepositoryRoot,
            "Tests",
            "compatibility-fixtures.json"));

        var fixture = Assert.Single(
            manifest.SelectedFixtures,
            value => value.Slug == "m2462362");
        Assert.Equal("m2462362.DNG", fixture.FileName);
        Assert.Equal(21700096, fixture.SizeBytes);
        Assert.Equal(
            "3fb74d1fc402daaa48a9c3f1d6279d7e85fb58dfdaf816f036927d49ab228977",
            fixture.Sha256);
        Assert.Equal(1, fixture.Expected!.Sensor!.Colors);
        Assert.Null(fixture.Expected.CameraFacts);
        Assert.True(fixture.Expected.MonochromeNeutrality);
    }

    [Fact]
    public void Classification_UsesOnlySensorColorCount()
    {
        Assert.True(RawBaseLoader.IsMonochromeSensor(
            Sensor(colors: 1, filters: uint.MaxValue, "RGBG")));
        Assert.False(RawBaseLoader.IsMonochromeSensor(
            Sensor(colors: 3, filters: 0, "G")));
    }

    [Theory]
    [InlineData(true, 1, true)]
    [InlineData(true, 3, false)]
    [InlineData(false, 3, true)]
    [InlineData(false, 1, false)]
    [InlineData(false, 4, false)]
    public void ProcessedLayout_MustMatchSensorClassification(
        bool isMonochrome,
        uint channels,
        bool expected) => Assert.Equal(
            expected,
            RawBaseLoader.HasExpectedProcessedLayout(isMonochrome, channels));

    [Fact]
    public void MonochromeOutput_DisablesColorProcessingAndPinsUnitMultipliers()
    {
        var configuration = RawBaseLoader.ConfigureOutput(
            BaseDecodeSettings.Default,
            preview: true,
            isMonochrome: true);

        Assert.Equal(0, configuration.OutputColor);
        Assert.False(configuration.HalfSize);
        Assert.False(configuration.UseCameraWhiteBalance);
        Assert.False(configuration.UseAutoWhiteBalance);
        Assert.False(configuration.UseCameraMatrix);
        Assert.Equal(
            [1f, 1f, 1f, 1f],
            [configuration.UserMultiplier0, configuration.UserMultiplier1,
             configuration.UserMultiplier2, configuration.UserMultiplier3]);
    }

    [Fact]
    public void GrayImport_ReplicatesEveryQ16SampleExactly()
    {
        ushort[] gray = [0, 1, 0x1234, 0xFEDC, ushort.MaxValue, 42];

        using var image = MonochromeRawImporter.ImportGray16(gray, 3, 2);
        var rgb = image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGB)!;

        Assert.Equal(ColorSpace.RGB, image.ColorSpace);
        for (var pixel = 0; pixel < gray.Length; pixel++)
        {
            Assert.Equal(gray[pixel], rgb[pixel * 3]);
            Assert.Equal(gray[pixel], rgb[pixel * 3 + 1]);
            Assert.Equal(gray[pixel], rgb[pixel * 3 + 2]);
        }
    }

    [Fact]
    public void GrayImport_ReplicatesEveryQ16SampleAcrossMultipleBands()
    {
        const int width = 8;
        const int height = 70_000;
        var gray = new ushort[width * height];
        for (var pixel = 0; pixel < gray.Length; pixel++)
            gray[pixel] = (ushort)((pixel * 251 + pixel / width) & ushort.MaxValue);

        using var image = MonochromeRawImporter.ImportGray16(gray, width, height);
        var rgb = image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGB)!;

        for (var pixel = 0; pixel < gray.Length; pixel++)
        {
            Assert.Equal(gray[pixel], rgb[pixel * 3]);
            Assert.Equal(gray[pixel], rgb[pixel * 3 + 1]);
            Assert.Equal(gray[pixel], rgb[pixel * 3 + 2]);
        }
    }

    [Fact]
    public void PreviewReduction_AreaAveragesTheGrayPlane()
    {
        ushort[] source =
        [
            0, 4, 8, 12,
            4, 8, 12, 16,
            16, 20, 24, 28,
            20, 24, 28, 32
        ];

        var reduced = MonochromeRawImporter.AreaAverageToMaxDimension(
            MemoryMarshal.AsBytes(source.AsSpan()),
            4,
            4,
            2,
            CancellationToken.None,
            out var width,
            out var height);

        Assert.Equal(2, width);
        Assert.Equal(2, height);
        Assert.Equal([4, 12, 20, 28], reduced);
    }

    private static LibRawSensorIdentity Sensor(
        int colors,
        uint filters,
        string description) => new(
            colors,
            filters,
            DngVersion: 0,
            XTrans: [],
            ColorDescription: description);
}
