using Avalonia.Media.Imaging;
using ImageMagick;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class RawThumbnailFallbackTests : IDisposable
{
    private readonly AvaloniaTestFixture _fixture;
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"HappyPhotonRawThumbFallback_{Guid.NewGuid():N}");

    public RawThumbnailFallbackTests(AvaloniaTestFixture fixture) =>
        _fixture = fixture;

    [WindowsFact]
    public async Task TonalOnlyFallbackMatchesUneditedSourceWithoutExtraction()
    {
        _fixture.RequireWindows();
        var (catalog, file) = await CreateCachedRawAsync(150, 90);
        using (catalog)
        {
            var raw = new CountingRawService();
            await using var renderedCache =
                new RenderedThumbnailCacheService(catalog);
            await using var service = new ThumbnailService(
                catalog,
                raw,
                new RenderPipeline(),
                renderedCache);
            file.EditSettings = new EditSettings { Exposure = 3, Saturation = 50 };

            using var uneditedResult = await service.LoadUneditedThumbnailAsync(file);
            using var fallbackResult = await service.LoadThumbnailAsync(file);
            var unedited = uneditedResult.Bitmap;
            var fallback = fallbackResult.Bitmap;

            Assert.NotNull(unedited);
            Assert.NotNull(fallback);
            Assert.Equal(
                BitmapConversionService.CopyBgraPixels(unedited!),
                BitmapConversionService.CopyBgraPixels(fallback!));
            Assert.Equal(0, raw.ExtractCount);
        }
    }

    [WindowsFact]
    public async Task GeometryFallbackRotatesWithoutChangingSourceCache()
    {
        _fixture.RequireWindows();
        var (catalog, file) = await CreateCachedRawAsync(150, 90);
        using (catalog)
        {
            var cachePath = catalog.GetThumbnailPath(file.CatalogId);
            var before = await File.ReadAllBytesAsync(cachePath);
            await using var renderedCache =
                new RenderedThumbnailCacheService(catalog);
            await using var service = new ThumbnailService(
                catalog,
                new CountingRawService(),
                new RenderPipeline(),
                renderedCache);
            file.EditSettings = new EditSettings { Rotation = 90 };

            using var fallbackResult = await service.LoadThumbnailAsync(file);
            var fallback = fallbackResult.Bitmap;

            Assert.NotNull(fallback);
            Assert.Equal(90, fallback!.PixelSize.Width);
            Assert.Equal(150, fallback.PixelSize.Height);
            Assert.Equal(before, await File.ReadAllBytesAsync(cachePath));
        }
    }

    [WindowsFact]
    public async Task GeometryFallbackDoesNotUpscaleCrop()
    {
        _fixture.RequireWindows();
        var (catalog, file) = await CreateCachedRawAsync(150, 100);
        using (catalog)
        {
            await using var renderedCache =
                new RenderedThumbnailCacheService(catalog);
            await using var service = new ThumbnailService(
                catalog,
                new CountingRawService(),
                new RenderPipeline(),
                renderedCache);
            file.EditSettings = new EditSettings
            {
                Crop = new CropRegion
                {
                    Left = 0.25,
                    Top = 0.25,
                    Right = 0.75,
                    Bottom = 0.75
                }
            };

            using var uneditedResult = await service.LoadUneditedThumbnailAsync(file);
            using var fallbackResult = await service.LoadThumbnailAsync(file);
            var unedited = uneditedResult.Bitmap;
            var fallback = fallbackResult.Bitmap;

            Assert.NotNull(unedited);
            Assert.NotNull(fallback);
            var expected = file.EditSettings.Crop!.ToPixels(
                unedited!.PixelSize.Width,
                unedited.PixelSize.Height);
            Assert.Equal(expected.Width, fallback!.PixelSize.Width);
            Assert.Equal(expected.Height, fallback.PixelSize.Height);
        }
    }

    [WindowsFact]
    public async Task AgentTonalBatchInLibraryDoesNotDecodeRawBase()
    {
        _fixture.RequireWindows();
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, $"{Guid.NewGuid():N}.dng");
        await File.WriteAllBytesAsync(path, [1]);
        using var catalog = new CatalogService(Path.Combine(
            _root,
            Guid.NewGuid().ToString("N")));
        await catalog.InitializeAsync();
        var loader = new CountingBaseLoader();
        await using var viewModel = new MainWindowViewModel(
            catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask);
        var file = new ImageFile(path);
        viewModel.Library.SetImages([file]);
        viewModel.SelectedImage = file;
        var patch = new AgentEditSettingsPatch(
            new EditSettings { Exposure = 1 },
            ApplyWb: false,
            ApplyBaseLook: false,
            ApplyHighlightReconstruction: false);

        var failures = await viewModel.ApplyAgentEditSettingsToImagesAsync(
            [file],
            patch);

        Assert.Empty(failures);
        Assert.Equal(0, loader.LoadCount);
    }

    [WindowsFact]
    public void LibRawPreview_FourByThreeIsCroppedToVisibleThreeByTwo()
    {
        _fixture.RequireWindows();
        var raw = new PreviewRawService(
            CreatePreview(400, 300, MagickColors.Green),
            6000,
            4000);
        var extractor = new EmbeddedPreviewExtractor(raw);

        using var bitmap = extractor.TryExtract(
            Path.Combine(_root, "not-an-image.dng"),
            ThumbnailService.ThumbnailSize,
            CancellationToken.None);

        Assert.NotNull(bitmap);
        Assert.Equal(ThumbnailService.ThumbnailSize, bitmap.PixelSize.Width);
        Assert.Equal(100, bitmap.PixelSize.Height);
    }

    [WindowsFact]
    public void LibRawPreview_CenterCropRemovesBothDistinguishableBorders()
    {
        _fixture.RequireWindows();
        var bytes = CreateBorderedPreview(
            480,
            360,
            borderSize: 20,
            verticalBorders: false);
        var extractor = new EmbeddedPreviewExtractor(
            new PreviewRawService(bytes, 6000, 4000));

        using var bitmap = extractor.TryExtract(
            Path.Combine(_root, "not-an-image.dng"),
            ThumbnailService.ThumbnailSize,
            CancellationToken.None);

        Assert.NotNull(bitmap);
        AssertPixelIsGreen(bitmap!, 0, 0);
        AssertPixelIsGreen(
            bitmap!,
            bitmap.PixelSize.Width - 1,
            bitmap.PixelSize.Height - 1);
    }

    [WindowsFact]
    public void LibRawPreview_MatchingAspectPassesThroughWithBorders()
    {
        _fixture.RequireWindows();
        var bytes = CreateBorderedPreview(
            300,
            200,
            borderSize: 20,
            verticalBorders: true);
        var extractor = new EmbeddedPreviewExtractor(
            new PreviewRawService(bytes, 6000, 4000));

        using var bitmap = extractor.TryExtract(
            Path.Combine(_root, "not-an-image.dng"),
            ThumbnailService.ThumbnailSize,
            CancellationToken.None);

        Assert.NotNull(bitmap);
        AssertPixelHasDominantChannel(bitmap!, 0, 50, redIndex: 2);
        AssertPixelHasDominantChannel(bitmap!, 149, 50, redIndex: 0);
    }

    [WindowsFact]
    public void LibRawPreview_PortraitUsesPortraitTargetRatio()
    {
        _fixture.RequireWindows();
        var extractor = new EmbeddedPreviewExtractor(
            new PreviewRawService(
                CreatePreview(300, 400, MagickColors.Green),
                4000,
                6000));

        using var bitmap = extractor.TryExtract(
            Path.Combine(_root, "not-an-image.dng"),
            ThumbnailService.ThumbnailSize,
            CancellationToken.None);

        Assert.NotNull(bitmap);
        Assert.Equal(100, bitmap.PixelSize.Width);
        Assert.Equal(ThumbnailService.ThumbnailSize, bitmap.PixelSize.Height);
    }

    [WindowsFact]
    public void LibRawPreview_LargeMismatchStillReturnsCroppedPreview()
    {
        _fixture.RequireWindows();
        var extractor = new EmbeddedPreviewExtractor(
            new PreviewRawService(
                CreatePreview(400, 100, MagickColors.Green),
                6000,
                4000));

        using var bitmap = extractor.TryExtract(
            Path.Combine(_root, "not-a-decodable-image.dng"),
            ThumbnailService.ThumbnailSize,
            CancellationToken.None);

        Assert.NotNull(bitmap);
        Assert.Equal(ThumbnailService.ThumbnailSize, bitmap.PixelSize.Width);
        Assert.Equal(100, bitmap.PixelSize.Height);
    }

    [Theory]
    [InlineData(null, 4000)]
    [InlineData(6000, null)]
    [InlineData(0, 4000)]
    [InlineData(6000, 0)]
    [InlineData(-1, 4000)]
    public void LibRawPreview_InvalidVisibleGeometrySkipsNormalization(
        int? visibleWidth,
        int? visibleHeight)
    {
        _fixture.RequireWindows();
        var extractor = new EmbeddedPreviewExtractor(
            new PreviewRawService(
                CreatePreview(400, 300, MagickColors.Green),
                visibleWidth,
                visibleHeight));

        using var bitmap = extractor.TryExtract(
            Path.Combine(_root, "not-an-image.dng"),
            ThumbnailService.ThumbnailSize,
            CancellationToken.None);

        Assert.NotNull(bitmap);
        Assert.Equal(ThumbnailService.ThumbnailSize, bitmap.PixelSize.Width);
        Assert.Equal(113, bitmap.PixelSize.Height);
    }

    [WindowsFact]
    public void UndersizedLibRawPreview_DoesNotStopLargerSafeCandidate()
    {
        _fixture.RequireWindows();
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "larger-preview.jpg");
        TestImages.WriteJpeg(path, MagickColors.Blue, 900, 600);
        var extractor = new EmbeddedPreviewExtractor(
            new PreviewRawService(
                CreatePreview(120, 80, MagickColors.Green),
                900,
                600));

        using var bitmap = extractor.TryExtract(
            path,
            512,
            CancellationToken.None);

        Assert.NotNull(bitmap);
        Assert.Equal(512, Math.Max(
            bitmap!.PixelSize.Width,
            bitmap.PixelSize.Height));
    }

    private async Task<(CatalogService Catalog, ImageFile File)> CreateCachedRawAsync(
        int width,
        int height)
    {
        Directory.CreateDirectory(_root);
        var sourcePath = Path.Combine(_root, $"{Guid.NewGuid():N}.dng");
        await File.WriteAllBytesAsync(sourcePath, [1]);
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-2));
        var catalog = new CatalogService(Path.Combine(_root, Guid.NewGuid().ToString("N")));
        await catalog.InitializeAsync();
        var file = new ImageFile(sourcePath);
        await file.EnsureCatalogIdAsync(catalog);
        var cachePath = catalog.GetThumbnailPath(file.CatalogId);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        TestImages.WriteJpeg(cachePath, width: (uint)width, height: (uint)height);
        return (catalog, file);
    }

    private static byte[] CreatePreview(
        int width,
        int height,
        MagickColor color)
    {
        using var image = new MagickImage(color, (uint)width, (uint)height);
        return image.ToByteArray(MagickFormat.Png);
    }

    private static byte[] CreateBorderedPreview(
        int width,
        int height,
        int borderSize,
        bool verticalBorders)
    {
        using var image = new MagickImage(
            MagickColors.Green,
            (uint)width,
            (uint)height);
        using var pixels = image.GetPixels();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var firstBorder = verticalBorders ? x < borderSize : y < borderSize;
                var secondBorder = verticalBorders
                    ? x >= width - borderSize
                    : y >= height - borderSize;
                if (firstBorder)
                {
                    pixels.SetPixel(x, y, [ushort.MaxValue, 0, 0]);
                }
                else if (secondBorder)
                {
                    pixels.SetPixel(x, y, [0, 0, ushort.MaxValue]);
                }
            }
        }

        return image.ToByteArray(MagickFormat.Png);
    }

    private static void AssertPixelIsGreen(Bitmap bitmap, int x, int y)
    {
        var pixels = BitmapConversionService.CopyBgraPixels(bitmap);
        var offset = (y * bitmap.PixelSize.Width + x) * 4;
        Assert.True(pixels[offset + 1] > pixels[offset]);
        Assert.True(pixels[offset + 1] > pixels[offset + 2]);
    }

    private static void AssertPixelHasDominantChannel(
        Bitmap bitmap,
        int x,
        int y,
        int redIndex)
    {
        var pixels = BitmapConversionService.CopyBgraPixels(bitmap);
        var offset = (y * bitmap.PixelSize.Width + x) * 4;
        Assert.True(pixels[offset + redIndex] > pixels[offset + 1]);
        Assert.True(pixels[offset + redIndex] > pixels[offset + (redIndex == 0 ? 2 : 0)]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class CountingRawService : IRawProcessingService
    {
        public int ExtractCount { get; private set; }
        public bool IsAvailable => true;
        public RawMetadata? ExtractMetadata(string filePath) => null;
        public RawThumbnailData? ExtractThumbnail(string filePath)
        {
            ExtractCount++;
            return null;
        }
    }

    private sealed class PreviewRawService : IRawProcessingService
    {
        private readonly RawThumbnailData _thumbnail;

        public PreviewRawService(
            byte[] encodedBytes,
            int? visibleWidth,
            int? visibleHeight) =>
            _thumbnail = new RawThumbnailData(
                encodedBytes,
                visibleWidth,
                visibleHeight);

        public bool IsAvailable => true;
        public RawThumbnailData? ExtractThumbnail(string filePath) => _thumbnail;
        public RawMetadata? ExtractMetadata(string filePath) => null;
    }

    private sealed class CountingBaseLoader : IBaseImageLoader
    {
        public int LoadCount { get; private set; }
        public bool CanLoad(ImageFile file) => true;
        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(ImageFile file, BaseDecodeSettings decode, CancellationToken cancellationToken) => BaseImageLoadOutcome.FromImage(LoadPreviewBase(file, decode, cancellationToken), BaseImageLoadFailure.UnsupportedRaw);
        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            LoadCount++;
            throw new InvalidOperationException("Library batch must not decode RAW.");
        }

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
