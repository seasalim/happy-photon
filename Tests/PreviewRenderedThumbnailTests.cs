using Avalonia.Media.Imaging;
using ImageMagick;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class PreviewRenderedThumbnailTests : IDisposable
{
    private readonly AvaloniaTestFixture _fixture;
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"HappyPhotonPreviewRenderedThumb_{Guid.NewGuid():N}");

    public PreviewRenderedThumbnailTests(AvaloniaTestFixture fixture) =>
        _fixture = fixture;

    [WindowsFact]
    public async Task AcceptedRawRenderPromotesLinearLightThumbnailByIdentity()
    {
        _fixture.RequireWindows();
        var (catalog, file) = await CreateFileAsync("source.dng");
        using (catalog)
        {
            var loader = new SolidLoader(isRaw: true);
            await using var service = CreateService(catalog, loader);
            var settings = new EditSettings { Exposure = 0.75, Saturation = 20 };
            var thumbnailCreated = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            service.RenderedThumbnailCreated += () => thumbnailCreated.TrySetResult();

            var (preview, _) = await service.ApplyEditsToPreviewAsync(
                file,
                settings,
                skipHistogram: true);
            Assert.NotNull(preview);
            preview!.Dispose();
            await thumbnailCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));

            using var promoted = service.TryPromoteRenderedThumbnail(file, settings);
            Assert.NotNull(promoted);
            Assert.Equal(192, promoted!.PixelSize.Width);
            Assert.Equal(128, promoted.PixelSize.Height);
            using var expected = CreateExpected(settings);
            Assert.Equal(
                BitmapConversionService.CopyBgraPixels(expected),
                BitmapConversionService.CopyBgraPixels(promoted));

            var other = new ImageFile(file.FilePath) { CatalogId = file.CatalogId };
            Assert.Null(service.TryPromoteRenderedThumbnail(other, settings));
            Assert.Null(service.TryPromoteRenderedThumbnail(
                file,
                new EditSettings { Exposure = 1 }));

            service.ClearPreviewCache();
            await service.DisposeAsync();
            var reader = new RenderedThumbnailCacheService(catalog);
            using var restored = reader.LoadMatching(
                file,
                RenderSettingsHash.Compute(settings));
            Assert.NotNull(restored);
            Assert.Equal(promoted.PixelSize, restored!.PixelSize);
            Assert.True(MeanAbsoluteDifference(promoted, restored) < 8);
            await reader.DisposeAsync();
        }
    }

    [WindowsFact]
    public async Task ReplacingRenderDisposesPreviousStrongThumbnail()
    {
        _fixture.RequireWindows();
        var (catalog, file) = await CreateFileAsync("replace.dng");
        using (catalog)
        {
            await using var service = CreateService(
                catalog,
                new SolidLoader(isRaw: true));
            var thumbnailCreated = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            service.RenderedThumbnailCreated += () => thumbnailCreated.TrySetResult();
            var (first, _) = await service.ApplyEditsToPreviewAsync(
                file,
                new EditSettings { Exposure = 0.25 },
                skipHistogram: true);
            await thumbnailCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var retained = service.GetRetainedThumbnailReference();
            Assert.NotNull(retained);
            Assert.True(retained!.TryGetTarget(out var oldThumbnail));

            var (second, _) = await service.ApplyEditsToPreviewAsync(
                file,
                new EditSettings { Exposure = 0.5 },
                skipHistogram: true);

            Assert.Throws<ObjectDisposedException>(
                () => _ = oldThumbnail!.PixelSize);
            first?.Dispose();
            second?.Dispose();
        }
    }

    [WindowsFact]
    public async Task DisposeWaitsForRenderedThumbnailCacheWrite()
    {
        _fixture.RequireWindows();
        var (catalog, file) = await CreateFileAsync("shutdown.dng");
        using (catalog)
        {
            var service = CreateService(catalog, new SolidLoader(isRaw: true));
            var settings = new EditSettings { Exposure = 0.5 };
            var (preview, _) = await service.ApplyEditsToPreviewAsync(
                file,
                settings,
                skipHistogram: true);
            preview?.Dispose();

            await service.DisposeAsync();

            var reader = new RenderedThumbnailCacheService(catalog);
            using var restored = reader.LoadMatching(
                file,
                RenderSettingsHash.Compute(settings));
            Assert.NotNull(restored);
            await reader.DisposeAsync();
        }
    }

    [WindowsFact]
    public async Task RenderedThumbnailWorkOverlapsCacheWriteAndWakesOnRestart()
    {
        _fixture.RequireWindows();
        var (catalog, file) = await CreateFileAsync("activity.dng");
        using (catalog)
        {
            var writerGate = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var producerRelease = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var cacheQueued = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var renderedCache = new RenderedThumbnailCacheService(
                catalog,
                2,
                Task.CompletedTask,
                TimeSpan.FromSeconds(5),
                writerGate.Task);
            await using var service = new PreviewService(
                catalog,
                new SolidLoader(isRaw: true),
                new RenderPipeline(),
                new HistogramService(),
                new PreviewCacheService(catalog),
                renderedCache);
            var wakeCount = 0;
            service.RenderedThumbnailWorkStarted += () => wakeCount++;
            service.RenderedThumbnailCacheQueuedAsync = () =>
            {
                cacheQueued.TrySetResult();
                return producerRelease.Task;
            };

            var (preview, _) = await service.ApplyEditsToPreviewAsync(
                file,
                new EditSettings { Exposure = 0.5 },
                skipHistogram: true);
            preview?.Dispose();
            await WaitUntilAsync(() => service.RenderedThumbnailTaskCount == 0);
            Assert.Equal(1, wakeCount);

            service.ClearPreviewCache();
            await cacheQueued.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => renderedCache.WriterInHandCount == 1);

            Assert.Equal(2, wakeCount);
            Assert.True(service.RenderedThumbnailTaskCount > 0);
            Assert.True(service.PendingCacheWrites > 0);

            producerRelease.SetResult();
            await WaitUntilAsync(() => service.RenderedThumbnailTaskCount == 0);
            Assert.True(service.PendingCacheWrites > 0);
            writerGate.SetResult();
            await renderedCache.DisposeAsync();
        }
    }

    [WindowsFact]
    public async Task StalePreviewPlaceholderIsNeverPromoted()
    {
        _fixture.RequireWindows();
        var (catalog, file) = await CreateFileAsync("placeholder.dng");
        using (catalog)
        {
            var settings = new EditSettings { Exposure = 0.5 };
            var writer = new PreviewCacheService(catalog);
            using var placeholder = new MagickImage(MagickColors.Purple, 320, 200);
            writer.QueueSaveToCache(
                file,
                placeholder,
                RenderSettingsHash.Compute(new EditSettings { Exposure = 0.25 }));
            await writer.DisposeAsync();

            var loader = new SolidLoader(isRaw: true);
            await using var service = CreateService(catalog, loader);
            using var cached = await service.LoadCachedPreviewAsync(file, settings);

            Assert.NotNull(cached);
            Assert.False(cached!.SettingsMatch);
            Assert.Null(service.TryPromoteRenderedThumbnail(file, settings));
            Assert.Equal(0, loader.LoadCount);
        }
    }

    [WindowsFact]
    public async Task NonRawRenderDoesNotRetainOrPromoteThumbnail()
    {
        _fixture.RequireWindows();
        var (catalog, file) = await CreateFileAsync("standard.jpg");
        using (catalog)
        {
            await using var service = CreateService(
                catalog,
                new SolidLoader(isRaw: false));
            var settings = new EditSettings { Exposure = 0.5 };
            var (preview, _) = await service.ApplyEditsToPreviewAsync(
                file,
                settings,
                skipHistogram: true);

            Assert.NotNull(preview);
            Assert.Null(service.GetRetainedThumbnailReference());
            Assert.Null(service.TryPromoteRenderedThumbnail(file, settings));
            preview?.Dispose();
        }
    }

    [WindowsFact]
    public async Task SupersededGenerationSkipsCandidateResize()
    {
        _fixture.RequireWindows();
        var (catalog, file) = await CreateFileAsync("superseded.dng");
        using (catalog)
        {
            var firstConverted = new ManualResetEventSlim();
            var releaseFirst = new ManualResetEventSlim();
            var conversionCount = 0;
            var candidateCount = 0;
            await using var service = new PreviewService(
                catalog,
                new SolidLoader(isRaw: true),
                new RenderPipeline(),
                new HistogramService(),
                new PreviewCacheService(catalog),
                new RenderedThumbnailCacheService(catalog));
            service.PreviewConverted += () =>
            {
                if (Interlocked.Increment(ref conversionCount) == 1)
                {
                    firstConverted.Set();
                    releaseFirst.Wait(TimeSpan.FromSeconds(5));
                }
            };
            service.RenderedThumbnailCreated += () =>
                Interlocked.Increment(ref candidateCount);

            var first = service.ApplyEditsToPreviewAsync(
                file,
                new EditSettings { Exposure = 0.25 },
                skipHistogram: true);
            Assert.True(firstConverted.Wait(TimeSpan.FromSeconds(5)));
            var second = service.ApplyEditsToPreviewAsync(
                file,
                new EditSettings { Exposure = 0.5 },
                skipHistogram: true);
            releaseFirst.Set();

            var firstResult = await first;
            var secondResult = await second;
            Assert.Null(firstResult.preview);
            Assert.NotNull(secondResult.preview);
            Assert.True(SpinWait.SpinUntil(
                () => Volatile.Read(ref candidateCount) == 1,
                TimeSpan.FromSeconds(5)));
            Assert.Equal(1, Volatile.Read(ref candidateCount));
            secondResult.preview?.Dispose();
        }
    }

    private async Task<(CatalogService Catalog, ImageFile File)> CreateFileAsync(
        string name)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, $"{Guid.NewGuid():N}-{name}");
        await File.WriteAllBytesAsync(path, [1]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-2));
        var catalog = new CatalogService(Path.Combine(_root, Guid.NewGuid().ToString("N")));
        await catalog.InitializeAsync();
        var file = new ImageFile(path);
        await file.EnsureCatalogIdAsync(catalog);
        return (catalog, file);
    }

    private static PreviewService CreateService(
        CatalogService catalog,
        IBaseImageLoader loader) =>
        new(
            catalog,
            loader,
            new RenderPipeline(),
            new HistogramService());

    private static Bitmap CreateExpected(EditSettings settings)
    {
        using var baseImage = SolidLoader.CreateBase(
            isRaw: true,
            BaseDecodeSettings.From(settings));
        using var rendered = new RenderPipeline().Render(new RenderRequest(
            baseImage,
            settings,
            RenderIntent.Preview,
            BaseImage.PreviewMaxDimension,
            new RenderOptions(false, false)));
        RenderColorEncoding.ResizeInLinearLight(rendered.Image, 192);
        return BitmapConversionService.ConvertToBitmap(rendered.Image)!;
    }

    private static double MeanAbsoluteDifference(Bitmap left, Bitmap right)
    {
        var first = BitmapConversionService.CopyBgraPixels(left);
        var second = BitmapConversionService.CopyBgraPixels(right);
        return first.Zip(second, (a, b) => Math.Abs(a - b)).Average();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for activity.");
            await Task.Delay(10);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class SolidLoader : IBaseImageLoader
    {
        private readonly bool _isRaw;
        private int _loadCount;

        public SolidLoader(bool isRaw) => _isRaw = isRaw;
        public int LoadCount => Volatile.Read(ref _loadCount);
        public bool CanLoad(ImageFile file) => true;

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(ImageFile file, BaseDecodeSettings decode, CancellationToken cancellationToken) => BaseImageLoadOutcome.FromImage(LoadPreviewBase(file, decode, cancellationToken), BaseImageLoadFailure.DecodeFailed);

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _loadCount);
            return CreateBase(_isRaw, decode);
        }

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public static BaseImage CreateBase(
            bool isRaw,
            BaseDecodeSettings decode) =>
            new(
                new MagickImage(MagickColors.Orange, 300, 200)
                {
                    Depth = 16,
                    ColorSpace = ColorSpace.RGB
                },
                new BaseImageInfo(
                    isRaw ? BaseSourceKind.RawLibRaw : BaseSourceKind.Standard,
                    isRaw,
                    decode,
                    null,
                    null,
                    isRaw ? 5500 : 6504,
                    0,
                    false,
                    null,
                    1,
                    300,
                    200));
    }
}
