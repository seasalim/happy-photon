using ImageMagick;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class ThumbnailServiceTests : IDisposable
{
    private readonly AvaloniaTestFixture _fixture;
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(), $"HappyPhotonThumbnailTests_{Guid.NewGuid():N}");

    public ThumbnailServiceTests(AvaloniaTestFixture fixture)
    {
        _fixture = fixture;
    }

    [WindowsFact]
    public async Task LoadUneditedThumbnailAsync_IgnoresDisplayEdits()
    {
        _fixture.RequireWindows();
        Directory.CreateDirectory(_tempDirectory);
        var sourcePath = Path.Combine(_tempDirectory, "source.jpg");
        using (var source = new MagickImage(MagickColors.Gray, 400, 200))
        {
            source.Write(sourcePath, MagickFormat.Jpeg);
        }

        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        await catalog.InitializeAsync();
        await using var imageService = new ImageService(catalog);
        var image = new ImageFile(sourcePath)
        {
            EditSettings = new EditSettings { Exposure = 3 }
        };

        using var uneditedResult = await imageService.LoadUneditedThumbnailAsync(
            image, CancellationToken.None);
        using var editedResult = await imageService.LoadThumbnailAsync(
            image, CancellationToken.None);
        var unedited = uneditedResult.Bitmap;
        var edited = editedResult.Bitmap;

        Assert.NotNull(unedited);
        Assert.NotNull(edited);
        var stats = new ImageStatsService();
        var uneditedStats = stats.Compute(
            BitmapConversionService.CreateEncodedSnapshot(unedited));
        var editedStats = stats.Compute(
            BitmapConversionService.CreateEncodedSnapshot(edited));
        Assert.True(uneditedStats.MeanLuminance < editedStats.MeanLuminance);
    }

    [WindowsFact]
    public async Task HeicPreviewAndThumbnail_BypassRawProcessing()
    {
        _fixture.RequireWindows();
        var heic = MagickFormatInfo.Create(MagickFormat.Heic);
        if (heic is not { SupportsReading: true })
        {
            Assert.Skip(
                "HEIC routing test requires an ImageMagick HEIC reader.");
        }

        Directory.CreateDirectory(_tempDirectory);
        var sourcePath = Path.Combine(
            GoldenTestPaths.AssetDirectory, "reference.heic");
        using var catalog = new CatalogService(
            Path.Combine(_tempDirectory, "catalog"));
        await catalog.InitializeAsync();
        var rawService = new RejectingRawProcessingService();
        await using var renderedCache =
            new RenderedThumbnailCacheService(catalog);
        await using var thumbnailService = new ThumbnailService(
            catalog,
            rawService,
            new RenderPipeline(),
            renderedCache);
        await using var previewService = new PreviewService(
            catalog,
            new StandardBaseLoader(),
            new RenderPipeline(),
            new HistogramService());
        var image = new ImageFile(sourcePath);

        using var thumbnailResult = await thumbnailService.LoadUneditedThumbnailAsync(image);
        var thumbnail = thumbnailResult.Bitmap;
        var (preview, _) = await previewService.LoadPreviewWithHistogramAsync(
            image, new EditSettings(), skipHistogram: true);
        using (preview)
        {
            Assert.NotNull(thumbnail);
            Assert.NotNull(preview);
        }

        Assert.Equal(0, rawService.CallCount);
    }

    [WindowsFact]
    public async Task RawWithoutSafeEmbeddedPreview_DoesNotUseMagickContainerDecode()
    {
        _fixture.RequireWindows();
        Directory.CreateDirectory(_tempDirectory);
        var sourcePath = Path.Combine(_tempDirectory, "no-preview.dng");
        using (var source = new MagickImage(MagickColors.Gray, 80, 60))
        {
            source.Write(sourcePath, MagickFormat.Tiff);
        }
        using var catalog = new CatalogService(
            Path.Combine(_tempDirectory, "raw-no-preview-catalog"));
        await catalog.InitializeAsync();
        await using var renderedCache =
            new RenderedThumbnailCacheService(catalog);
        await using var service = new ThumbnailService(
            catalog,
            new UnavailableRawProcessingService(),
            new RenderPipeline(),
            renderedCache);

        using var result = await service.LoadUneditedThumbnailAsync(
            new ImageFile(sourcePath));

        Assert.Equal(ThumbnailLoadStatus.Failed, result.Status);
        Assert.Null(result.Bitmap);
    }

    [Theory]
    [InlineData(LibraryThumbnailSize.Small, 150)]
    [InlineData(LibraryThumbnailSize.Medium, 192)]
    [InlineData(LibraryThumbnailSize.Large, 512)]
    public async Task CacheMiss_GeneratesRequestedLongEdge(
        LibraryThumbnailSize size,
        int expectedLongEdge)
    {
        _fixture.RequireWindows();
        Directory.CreateDirectory(_tempDirectory);
        var sourcePath = Path.Combine(_tempDirectory, $"source-{size}.jpg");
        using (var source = new MagickImage(MagickColors.Gray, 1200, 800))
        {
            source.Write(sourcePath, MagickFormat.Jpeg);
        }

        using var catalog = new CatalogService(
            Path.Combine(_tempDirectory, $"catalog-{size}"));
        await catalog.InitializeAsync();
        await using var imageService = new ImageService(catalog);
        var image = new ImageFile(sourcePath);
        var request = ThumbnailSizeRequest.For(size);

        using var result = await imageService.LoadUneditedThumbnailAsync(
            image,
            request,
            CancellationToken.None);

        Assert.Equal(expectedLongEdge, Math.Max(
            result.PixelDimensions.Width,
            result.PixelDimensions.Height));
        Assert.True(result.SatisfiesMinimumDimension);
        Assert.Equal(request, result.Request);
    }

    [WindowsFact]
    public async Task WarmPlaceholderCanBeFollowedBySourceQualityUpgrade()
    {
        _fixture.RequireWindows();
        Directory.CreateDirectory(_tempDirectory);
        var sourcePath = Path.Combine(_tempDirectory, "upgrade-source.jpg");
        using (var source = new MagickImage(MagickColors.Gray, 1200, 800))
        {
            source.Write(sourcePath, MagickFormat.Jpeg);
        }
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-2));
        using var catalog = new CatalogService(
            Path.Combine(_tempDirectory, "upgrade-catalog"));
        await catalog.InitializeAsync();
        var image = new ImageFile(sourcePath);
        await image.EnsureCatalogIdAsync(catalog);
        var cachePath = catalog.GetThumbnailPath(image.CatalogId);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        using (var cached = new MagickImage(MagickColors.Gray, 150, 100))
        {
            cached.Write(cachePath, MagickFormat.Jpeg);
        }
        File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow);
        await using var imageService = new ImageService(catalog);
        var request = ThumbnailSizeRequest.For(LibraryThumbnailSize.Large);

        using var placeholder = await imageService.LoadThumbnailAsync(
            image,
            request,
            CancellationToken.None);
        using var upgraded = await imageService.LoadThumbnailAsync(
            image,
            request,
            allowUndersizedCachePlaceholder: false,
            CancellationToken.None);

        Assert.Equal(150, Math.Max(
            placeholder.PixelDimensions.Width,
            placeholder.PixelDimensions.Height));
        Assert.Equal(512, Math.Max(
            upgraded.PixelDimensions.Width,
            upgraded.PixelDimensions.Height));
        Assert.True(upgraded.SatisfiesMinimumDimension);
    }

    [WindowsFact]
    public async Task EditedCropBelowMinimumTerminatesIdenticalUpgrade()
    {
        _fixture.RequireWindows();
        Directory.CreateDirectory(_tempDirectory);
        var sourcePath = Path.Combine(_tempDirectory, "cropped-source.jpg");
        using (var source = new MagickImage(MagickColors.Gray, 1200, 800))
        {
            source.Write(sourcePath, MagickFormat.Jpeg);
        }

        using var catalog = new CatalogService(
            Path.Combine(_tempDirectory, "cropped-catalog"));
        await catalog.InitializeAsync();
        await using var imageService = new ImageService(catalog);
        var image = new ImageFile(sourcePath)
        {
            EditSettings = new EditSettings
            {
                Crop = new CropRegion
                {
                    Left = 0.25,
                    Top = 0.25,
                    Right = 0.75,
                    Bottom = 0.75
                }
            }
        };
        var request = ThumbnailSizeRequest.For(LibraryThumbnailSize.Large);

        using var result = await imageService.LoadThumbnailAsync(
            image,
            request,
            CancellationToken.None);

        Assert.False(result.SatisfiesMinimumDimension);
        Assert.True(result.SourceCannotProvideRequestedQuality);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private sealed class RejectingRawProcessingService : IRawProcessingService
    {
        private int _callCount;

        public bool IsAvailable => true;

        public int CallCount => Volatile.Read(ref _callCount);

        public RawThumbnailData? ExtractThumbnail(string filePath) =>
            Reject<RawThumbnailData>();

        public RawMetadata? ExtractMetadata(string filePath) => Reject<RawMetadata>();

        private T? Reject<T>() where T : class
        {
            Interlocked.Increment(ref _callCount);
            throw new InvalidOperationException("HEIC must not use RAW processing.");
        }

        private MagickImage? Reject() => Reject<MagickImage>();
    }

    private sealed class UnavailableRawProcessingService : IRawProcessingService
    {
        public bool IsAvailable => false;
        public RawThumbnailData? ExtractThumbnail(string filePath) => null;
        public RawMetadata? ExtractMetadata(string filePath) => null;
    }
}
