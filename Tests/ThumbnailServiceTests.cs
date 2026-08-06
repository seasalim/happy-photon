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

        using var unedited = await imageService.LoadUneditedThumbnailAsync(
            image, CancellationToken.None);
        using var edited = await imageService.LoadThumbnailAsync(
            image, CancellationToken.None);

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
            throw Xunit.Sdk.SkipException.ForSkip(
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

        using var thumbnail = await thumbnailService.LoadUneditedThumbnailAsync(image);
        var (preview, _) = await previewService.LoadPreviewWithHistogramAsync(
            image, new EditSettings(), skipHistogram: true);
        using (preview)
        {
            Assert.NotNull(thumbnail);
            Assert.NotNull(preview);
        }

        Assert.Equal(0, rawService.CallCount);
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

        public byte[]? ExtractThumbnail(string filePath) => Reject<byte[]>();

        public RawMetadata? ExtractMetadata(string filePath) => Reject<RawMetadata>();

        private T? Reject<T>() where T : class
        {
            Interlocked.Increment(ref _callCount);
            throw new InvalidOperationException("HEIC must not use RAW processing.");
        }

        private MagickImage? Reject() => Reject<MagickImage>();
    }
}
