using ImageMagick;
using ImageMagick.Formats;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ImageExportServiceVariantTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(), $"HappyPhotonExportTests_{Guid.NewGuid():N}");

    public ImageExportServiceVariantTests() => Directory.CreateDirectory(_tempDirectory);

    [Fact]
    public async Task ExportBatch_WritesProgressivelySizedWebpVariants()
    {
        var sourcePath = WriteSourceImage();
        var outputFolder = Path.Combine(_tempDirectory, "exports");
        var settings = new ExportSettings
        {
            OutputFolder = outputFolder,
            Format = ExportFormat.Webp,
            ExportWeb = true,
            ExportSmall = true,
            WebMaxSize = 200,
            SmallMaxSize = 100
        };
        var service = CreateService();

        var count = await service.ExportBatchAsync([new ImageFile(sourcePath)], settings);

        Assert.Equal(1, count);
        AssertImage(Path.Combine(outputFolder, "hi-res", "source.webp"), 400, 200);
        AssertImage(Path.Combine(outputFolder, "web", "source.webp"), 200, 100);
        AssertImage(Path.Combine(outputFolder, "small", "source.webp"), 100, 50);
    }

    [Fact]
    public async Task ExportBatch_SinglePngVariantStaysFlat()
    {
        var sourcePath = WriteSourceImage();
        var outputFolder = Path.Combine(_tempDirectory, "exports");
        var settings = new ExportSettings
        {
            OutputFolder = outputFolder,
            Format = ExportFormat.Png
        };
        var service = CreateService();

        await service.ExportBatchAsync([new ImageFile(sourcePath)], settings);

        AssertImage(Path.Combine(outputFolder, "source.png"), 400, 200);
        Assert.False(Directory.Exists(Path.Combine(outputFolder, "hi-res")));
    }

    [Fact]
    public async Task ExportBatch_UnorderedVariantsResizeLargestFirst()
    {
        var sourcePath = WriteSourceImage();
        var outputFolder = Path.Combine(_tempDirectory, "unordered");
        var settings = new ExportSettings
        {
            OutputFolder = outputFolder,
            Format = ExportFormat.Png
        };

        await CreateService().ExportBatchAsync(
            [new ImageFile(sourcePath)],
            settings,
            [
                new ExportVariant("small", 100),
                new ExportVariant("large", 300)
            ],
            useSubfolders: true);

        AssertImage(Path.Combine(outputFolder, "large", "source.png"), 300, 150);
        AssertImage(Path.Combine(outputFolder, "small", "source.png"), 100, 50);
    }

    [Fact]
    public async Task ExportBatch_PngOutputIsEightBit()
    {
        var sourcePath = WriteSourceImage();
        var outputFolder = Path.Combine(_tempDirectory, "png-depth");
        var settings = new ExportSettings
        {
            OutputFolder = outputFolder,
            Format = ExportFormat.Png
        };

        await CreateService().ExportBatchAsync(
            [new ImageFile(sourcePath)],
            settings);

        using var exported = new MagickImage(
            Path.Combine(outputFolder, "source.png"));
        Assert.Equal(8u, exported.Depth);
    }

    [Fact]
    public void PngEncoding_UsesResponsiveLosslessCompression()
    {
        var defines = ExportEncoder.CreatePngWriteDefines();

        Assert.Equal(3u, defines.CompressionLevel);
        Assert.Equal(
            PngCompressionStrategy.Adaptive,
            defines.CompressionStrategy);
    }

    [Theory]
    [InlineData(80, "2x2,1x1,1x1")]
    [InlineData(92, "1x1,1x1,1x1")]
    public async Task ExportBatch_JpegPinsSamplingAndBaseline(
        int quality,
        string expectedSampling)
    {
        var sourcePath = WriteSourceImage();
        var outputFolder = Path.Combine(
            _tempDirectory,
            $"jpeg-{quality}");
        var settings = new ExportSettings
        {
            OutputFolder = outputFolder,
            Quality = quality
        };

        await CreateService().ExportBatchAsync(
            [new ImageFile(sourcePath)],
            settings);

        using var exported = new MagickImage(
            Path.Combine(outputFolder, "source.jpg"));
        Assert.Equal(Interlace.NoInterlace, exported.Interlace);
        Assert.Equal(
            expectedSampling,
            exported.GetAttribute("jpeg:sampling-factor"));
    }

    [Fact]
    public async Task ExportBatch_PixelsMatchSharedRenderPipeline()
    {
        var sourcePath = Path.Combine(_tempDirectory, "render-source.dng");
        File.WriteAllBytes(sourcePath, []);
        var outputFolder = Path.Combine(_tempDirectory, "render-parity");
        var file = new ImageFile(sourcePath)
        {
            EditSettings = new EditSettings
            {
                Exposure = 0.5,
                Contrast = 20,
                Saturation = 10
            }
        };
        var settings = new ExportSettings
        {
            OutputFolder = outputFolder,
            Format = ExportFormat.Png
        };
        var loader = new StubBaseLoader();

        await new ImageExportService(
            new RenderPipeline(),
            loader,
            new ExportMetadataService()).ExportBatchAsync([file], settings);

        using var baseImage = loader.LoadFullBase(
            file,
            BaseDecodeSettings.Default,
            CancellationToken.None);
        using var expected = new RenderPipeline().Render(new RenderRequest(
            baseImage!,
            file.EditSettings,
            RenderIntent.Export,
            null,
            new RenderOptions(false, false)));
        using var actual = new MagickImage(Path.Combine(
            outputFolder,
            "render-source.png"));

        AssertPixelsWithinOne(expected.Image, actual);
    }

    [Fact]
    public async Task ExportBatch_SnapshotsSettingsBeforeDecode()
    {
        var sourcePath = Path.Combine(_tempDirectory, "snapshot.dng");
        File.WriteAllBytes(sourcePath, []);
        var outputFolder = Path.Combine(_tempDirectory, "snapshot");
        var snapshot = new EditSettings
        {
            Exposure = 0.5,
            Wb = new WhiteBalanceSettings
            {
                Mode = WbMode.Picked,
                Gains = [1.1, 1.0, 0.9]
            }
        };
        var file = new ImageFile(sourcePath)
        {
            EditSettings = snapshot.Clone()
        };
        var loader = new StubBaseLoader
        {
            OnLoadFullBase = loadedFile =>
            {
                loadedFile.EditSettings.Exposure = -1;
                loadedFile.EditSettings.Wb = new WhiteBalanceSettings();
            }
        };
        var settings = new ExportSettings
        {
            OutputFolder = outputFolder,
            Format = ExportFormat.Png
        };

        await new ImageExportService(
            new RenderPipeline(),
            loader,
            new ExportMetadataService()).ExportBatchAsync([file], settings);

        Assert.Equal(
            BaseDecodeSettings.From(snapshot).CacheKey,
            loader.LastDecode?.CacheKey);
        using var baseImage = loader.LoadFullBase(
            file,
            BaseDecodeSettings.From(snapshot),
            CancellationToken.None);
        using var expected = new RenderPipeline().Render(new RenderRequest(
            baseImage!,
            snapshot,
            RenderIntent.Export,
            null,
            new RenderOptions(false, false)));
        using var actual = new MagickImage(Path.Combine(
            outputFolder,
            "snapshot.png"));
        AssertPixelsWithinOne(expected.Image, actual);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ExportBatch_StripLocationDataControlsGpsIfd(
        bool stripLocationData,
        bool expectGps)
    {
        var sourcePath = Path.Combine(
            GoldenTestPaths.AssetDirectory,
            "srgb-exif-gps-orientation-6.jpg");
        var outputFolder = Path.Combine(
            _tempDirectory,
            stripLocationData ? "stripped" : "retained");
        var settings = new ExportSettings
        {
            OutputFolder = outputFolder,
            StripLocationData = stripLocationData
        };
        var service = CreateService();

        await service.ExportBatchAsync([new ImageFile(sourcePath)], settings);

        using var exported = new MagickImage(Path.Combine(
            outputFolder, "srgb-exif-gps-orientation-6.jpg"));
        var profile = exported.GetExifProfile();
        Assert.NotNull(profile);
        Assert.NotNull(profile!.GetValue(ExifTag.DateTimeOriginal));
        Assert.Equal(
            expectGps,
            profile.Values.Any(value => value.Tag.Ifd == ExifIfds.Gps));
        Assert.Equal(
            expectGps,
            profile.GetValue(ExifTag.GPSLatitude) != null);
    }

    [Theory]
    [InlineData(ExportFormat.Jpeg, ".jpg")]
    [InlineData(ExportFormat.Png, ".png")]
    [InlineData(ExportFormat.Webp, ".webp")]
    public async Task ExportBatch_UntaggedSourceEmbedsSrgbProfile(
        ExportFormat format,
        string extension)
    {
        var sourcePath = WriteSourceImage();
        var outputFolder = Path.Combine(_tempDirectory, $"untagged-{format}");
        var settings = new ExportSettings
        {
            OutputFolder = outputFolder,
            Format = format
        };
        var service = CreateService();

        await service.ExportBatchAsync([new ImageFile(sourcePath)], settings);

        AssertSrgbProfile(Path.Combine(outputFolder, $"source{extension}"));
    }

    [Theory]
    [InlineData("display-p3-reference.jpg", ExportFormat.Jpeg, ".jpg")]
    [InlineData("display-p3-reference.jpg", ExportFormat.Png, ".png")]
    [InlineData("display-p3-reference.jpg", ExportFormat.Webp, ".webp")]
    [InlineData("adobe-rgb-reference.jpg", ExportFormat.Jpeg, ".jpg")]
    [InlineData("adobe-rgb-reference.jpg", ExportFormat.Png, ".png")]
    [InlineData("adobe-rgb-reference.jpg", ExportFormat.Webp, ".webp")]
    public async Task ExportBatch_TaggedStandardSourceEmbedsSrgbProfile(
        string assetName,
        ExportFormat format,
        string extension)
    {
        var sourcePath = Path.Combine(GoldenTestPaths.AssetDirectory, assetName);
        var outputFolder = Path.Combine(_tempDirectory, $"tagged-{assetName}");
        var settings = new ExportSettings
        {
            OutputFolder = outputFolder,
            Format = format
        };
        var service = CreateService();

        await service.ExportBatchAsync([new ImageFile(sourcePath)], settings);

        AssertSrgbProfile(Path.Combine(
            outputFolder,
            $"{Path.GetFileNameWithoutExtension(assetName)}{extension}"));
    }

    [Fact]
    public async Task ExportBatch_RawSourceEmbedsSrgbProfile()
    {
        var sourcePath = Path.Combine(_tempDirectory, "source.dng");
        File.WriteAllBytes(sourcePath, []);
        var outputFolder = Path.Combine(_tempDirectory, "raw");
        var settings = new ExportSettings { OutputFolder = outputFolder };
        var service = new ImageExportService(
            new RenderPipeline(),
            new StubBaseLoader(),
            new ExportMetadataService());

        await service.ExportBatchAsync([new ImageFile(sourcePath)], settings);

        AssertSrgbProfile(Path.Combine(outputFolder, "source.jpg"));
    }

    private string WriteSourceImage()
    {
        var path = Path.Combine(_tempDirectory, "source.png");
        using var image = new MagickImage(MagickColors.Orange, 400, 200);
        image.Write(path);
        return path;
    }

    private static ImageExportService CreateService() =>
        new(
            new RenderPipeline(),
            new BaseLoaderRouter(
                new RawBaseLoader(),
                new StandardBaseLoader()),
            new ExportMetadataService());

    private static void AssertImage(string path, uint width, uint height)
    {
        Assert.True(File.Exists(path));
        using var image = new MagickImage(path);
        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
    }

    private static void AssertSrgbProfile(string path)
    {
        var readSettings = new MagickReadSettings();
        if (Path.GetExtension(path).Equals(
            ".png",
            StringComparison.OrdinalIgnoreCase))
        {
            readSettings.SetDefine(
                MagickFormat.Png,
                "preserve-iCCP",
                "true");
        }
        using var image = new MagickImage(path, readSettings);
        var profile = image.GetColorProfile();
        Assert.NotNull(profile);
        Assert.Equal(
            ColorProfiles.SRGB.ToByteArray(),
            profile!.ToByteArray());
    }

    private static void AssertPixelsWithinOne(
        MagickImage expected,
        MagickImage actual)
    {
        var expectedPixels = expected.GetPixelsUnsafe()
            .ToByteArray(PixelMapping.RGB) ??
            throw new InvalidOperationException("Expected pixels unavailable.");
        var actualPixels = actual.GetPixelsUnsafe()
            .ToByteArray(PixelMapping.RGB) ??
            throw new InvalidOperationException("Actual pixels unavailable.");
        Assert.Equal(expectedPixels.Length, actualPixels.Length);
        Assert.All(
            expectedPixels.Zip(actualPixels),
            pair => Assert.InRange(
                Math.Abs(pair.First - pair.Second),
                0,
                1));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    private sealed class StubBaseLoader : IBaseImageLoader
    {
        public Action<ImageFile>? OnLoadFullBase { get; init; }
        public BaseDecodeSettings? LastDecode { get; private set; }

        public bool CanLoad(ImageFile file) => true;

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(ImageFile file, BaseDecodeSettings decode, CancellationToken cancellationToken) => BaseImageLoadOutcome.FromImage(LoadPreviewBase(file, decode, cancellationToken), BaseImageLoadFailure.DecodeFailed);

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            LastDecode = decode;
            OnLoadFullBase?.Invoke(file);
            return new(
                new MagickImage(MagickColors.Orange, 64, 48)
                {
                    Depth = 16,
                    ColorSpace = ColorSpace.RGB
                },
                new BaseImageInfo(
                    BaseSourceKind.RawLibRaw,
                    true,
                    decode,
                    null,
                    null,
                    5500,
                    0,
                    false,
                    null,
                    1,
                    64,
                    48));
        }
    }
}
