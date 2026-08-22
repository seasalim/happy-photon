using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class TiffExportTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-tiff-{Guid.NewGuid():N}")).FullName;

    [Theory]
    [InlineData(OutputColorSpace.Srgb)]
    [InlineData(OutputColorSpace.DisplayP3)]
    public void Encode_RoundTripsPreEncodeQ16AndProfileExactly(
        OutputColorSpace outputColorSpace)
    {
        using var preEncode = CreateQ16Image(hasAlpha: false);
        var expectedPixels = ReadRgb(preEncode);
        var expectedProfile = OutputColorProfiles.Get(
            outputColorSpace).ToByteArray();
        var path = Path.Combine(_root, $"parity-{outputColorSpace}.tif");

        ExportEncoder.Write(
            preEncode,
            new ExportSettings { Format = ExportFormat.Tiff },
            outputColorSpace,
            path);

        using var decoded = new MagickImage(path);
        Assert.Equal(16u, decoded.Depth);
        Assert.Equal(CompressionMethod.Zip, decoded.Compression);
        Assert.False(decoded.HasAlpha);
        Assert.Equal(expectedPixels, ReadRgb(decoded));
        Assert.Equal(
            expectedProfile,
            decoded.GetColorProfile()!.ToByteArray());
    }

    [Fact]
    public void Encode_DropsAlphaFromAlphaBearingInput()
    {
        using var preEncode = CreateQ16Image(hasAlpha: true);
        Assert.True(preEncode.HasAlpha);
        var path = Path.Combine(_root, "no-alpha.tif");

        ExportEncoder.Write(
            preEncode,
            new ExportSettings { Format = ExportFormat.Tiff },
            OutputColorSpace.Srgb,
            path);

        using var decoded = new MagickImage(path);
        Assert.False(decoded.HasAlpha);
        Assert.Equal(3u, decoded.ChannelCount);
    }

    [Fact]
    public async Task ExportBatch_VariantsResizeAndSharpenLargestFirst()
    {
        var sourcePath = Path.Combine(_root, "source.png");
        using (var source = new MagickImage(MagickColors.Orange, 400, 200))
        {
            source.Write(sourcePath);
        }
        var outputFolder = Path.Combine(_root, "variants");
        var settings = new ExportSettings
        {
            OutputFolder = outputFolder,
            Format = ExportFormat.Tiff,
            OutputSharpening = OutputSharpeningMode.Screen
        };
        var service = new ImageExportService(
            new RenderPipeline(),
            new StandardBaseLoader(),
            new ExportMetadataService());

        var count = await service.ExportBatchAsync(
            [new ImageFile(sourcePath)],
            settings,
            [
                new ExportVariant("small", 100),
                new ExportVariant("large", 300)
            ],
            useSubfolders: true);

        Assert.Equal(1, count);
        AssertTiff(
            Path.Combine(outputFolder, "large", "source.tif"),
            300,
            150);
        AssertTiff(
            Path.Combine(outputFolder, "small", "source.tif"),
            100,
            50);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ExportBatch_PreservesOrStripsGpsWithNormalizedOrientation(
        bool stripLocationData,
        bool expectGps)
    {
        var sourcePath = Path.Combine(
            GoldenTestPaths.AssetDirectory,
            "srgb-exif-gps-orientation-6.jpg");
        var outputFolder = Path.Combine(_root, $"gps-{stripLocationData}");
        var settings = new ExportSettings
        {
            OutputFolder = outputFolder,
            Format = ExportFormat.Tiff,
            StripLocationData = stripLocationData
        };
        var service = new ImageExportService(
            new RenderPipeline(),
            new StandardBaseLoader(),
            new ExportMetadataService());

        await service.ExportBatchAsync(
            [new ImageFile(sourcePath)],
            settings);

        using var exported = new MagickImage(Path.Combine(
            outputFolder,
            "srgb-exif-gps-orientation-6.tif"));
        Assert.Equal(OrientationType.TopLeft, exported.Orientation);
        Assert.Equal(
            expectGps,
            exported.GetAttribute("exif:GPSLatitude") != null);
    }

    private static MagickImage CreateQ16Image(bool hasAlpha)
    {
        const int width = 31;
        const int height = 17;
        var channels = hasAlpha ? 4 : 3;
        var values = new ushort[width * height * channels];
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            var offset = pixel * channels;
            values[offset] = checked((ushort)((pixel * 977 + 123) % 65536));
            values[offset + 1] = checked((ushort)((pixel * 1597 + 456) % 65536));
            values[offset + 2] = checked((ushort)((pixel * 2137 + 789) % 65536));
            if (hasAlpha)
            {
                values[offset + 3] = checked(
                    (ushort)((pixel * 3253 + 1011) % 65536));
            }
        }

        var image = new MagickImage(
            hasAlpha ? MagickColors.Transparent : MagickColors.Black,
            width,
            height)
        {
            ColorSpace = ColorSpace.sRGB
        };
        using var pixels = image.GetPixels();
        pixels.SetArea(0, 0, width, height, values);
        return image;
    }

    private static ushort[] ReadRgb(MagickImage image) =>
        image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGB) ??
        throw new InvalidOperationException("Unable to read RGB pixels.");

    private static void AssertTiff(string path, uint width, uint height)
    {
        using var image = new MagickImage(path);
        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
        Assert.Equal(16u, image.Depth);
        Assert.Equal(CompressionMethod.Zip, image.Compression);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
