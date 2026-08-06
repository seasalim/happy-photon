using Avalonia.Media.Imaging;
using System.Collections.Concurrent;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class PreviewServiceConcurrencyTests : IDisposable
{
    private readonly AvaloniaTestFixture _fixture;
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"HappyPhotonPreviewConcurrency_{Guid.NewGuid():N}");

    public PreviewServiceConcurrencyTests(AvaloniaTestFixture fixture)
    {
        _fixture = fixture;
    }

    [WindowsFact]
    public async Task SharedDecode_RendersOnlyNewestWaiter()
    {
        _fixture.RequireWindows();
        var (catalog, file) = await CreateCatalogAndFile();
        using (catalog)
        {
            var loader = new BlockingLoader();
            var renderCount = 0;
            await using var service = CreateService(
                catalog,
                loader,
                () => Interlocked.Increment(ref renderCount));

            var first = service.ApplyEditsToPreviewAsync(
                file,
                new EditSettings(),
                skipHistogram: true);
            Assert.True(loader.Started.Wait(TimeSpan.FromSeconds(5)));
            var second = service.ApplyEditsToPreviewAsync(
                file,
                new EditSettings { Exposure = 1 },
                skipHistogram: true);
            loader.Release.Set();

            var firstResult = await first;
            var secondResult = await second;
            firstResult.preview?.Dispose();
            secondResult.preview?.Dispose();

            Assert.Null(firstResult.preview);
            Assert.NotNull(secondResult.preview);
            Assert.Equal(1, renderCount);
            Assert.Equal(1, loader.DecodeCount);
        }
    }

    [WindowsFact]
    public async Task RequestSettings_AreSnapshottedBeforeDecode()
    {
        _fixture.RequireWindows();
        var (catalog, file) = await CreateCatalogAndFile();
        using (catalog)
        {
            var loader = new BlockingLoader();
            var mutable = new EditSettings();
            var expectedHash = RenderSettingsHash.Compute(mutable.Clone());
            var cache = new PreviewCacheService(catalog);
            await using var service = new PreviewService(
                catalog,
                loader,
                new RenderPipeline(),
                new HistogramService(),
                cache,
                new RenderedThumbnailCacheService(catalog));

            var request = service.ApplyEditsToPreviewAsync(
                file,
                mutable,
                skipHistogram: true);
            Assert.True(loader.Started.Wait(TimeSpan.FromSeconds(5)));
            mutable.Exposure = 2;
            loader.Release.Set();

            var result = await request;
            Assert.NotNull(result.preview);
            service.ClearPreviewCache();
            result.preview!.Dispose();
            await service.DisposeAsync();

            Assert.Equal(
                expectedHash,
                File.ReadAllText(cache.GetMetadataPath(file)));
        }
    }

    [WindowsFact]
    public async Task DecodeTransition_RendersOldBaseThenRaisesOneLatestRefresh()
    {
        _fixture.RequireWindows();
        var (catalog, file) = await CreateCatalogAndFile();
        using (catalog)
        {
            var loader = new DecodeTransitionLoader();
            await using var service = new PreviewService(
                catalog,
                loader,
                new RenderPipeline(),
                new HistogramService());
            var refreshReady =
                new TaskCompletionSource<Bitmap>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            var refreshStarted =
                new TaskCompletionSource<PreviewBaseRefreshState>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            var refreshCompleted =
                new TaskCompletionSource<PreviewBaseRefreshState>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            var refreshStates =
                new ConcurrentQueue<PreviewBaseRefreshState>();
            var refreshCount = 0;
            service.PreviewRefreshed += (_, refresh) =>
            {
                Interlocked.Increment(ref refreshCount);
                refreshReady.TrySetResult(refresh.DetachBitmap());
            };
            service.BaseRefreshStateChanged += (_, state) =>
            {
                refreshStates.Enqueue(state);
                if (state.IsRefreshing)
                {
                    refreshStarted.TrySetResult(state);
                }
                else
                {
                    refreshCompleted.TrySetResult(state);
                }
            };

            var (initial, _) = await service.ApplyEditsToPreviewAsync(
                file,
                new EditSettings(),
                skipHistogram: true);
            Assert.NotNull(initial);

            var decodeSettings = new EditSettings
            {
                HlReconstruction = HlReconstructionMode.Blend
            };
            var firstStale = service.ApplyEditsToPreviewAsync(
                file,
                decodeSettings,
                skipHistogram: true);
            Assert.True(loader.ReplacementStarted.Wait(TimeSpan.FromSeconds(5)));
            var latestSettings = decodeSettings.Clone();
            latestSettings.Exposure = 1;
            var latestStale = service.ApplyEditsToPreviewAsync(
                file,
                latestSettings,
                skipHistogram: true);

            var firstResult = await firstStale;
            var latestResult = await latestStale;
            Assert.Null(firstResult.preview);
            Assert.NotNull(latestResult.preview);
            Assert.False(refreshReady.Task.IsCompleted);
            var started = await refreshStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5));
            await Task.Delay(200);
            Assert.False(refreshCompleted.Task.IsCompleted);

            loader.ReleaseReplacement.Set();
            using var refreshed = await refreshReady.Task.WaitAsync(
                TimeSpan.FromSeconds(5));
            var completed = await refreshCompleted.Task.WaitAsync(
                TimeSpan.FromSeconds(5));
            await Task.Delay(50);

            Assert.Equal(1, Volatile.Read(ref refreshCount));
            Assert.Same(file, started.ImageFile);
            Assert.Same(file, completed.ImageFile);
            Assert.Equal(started.RequestId, completed.RequestId);
            Assert.Collection(
                refreshStates,
                state => Assert.True(state.IsRefreshing),
                state => Assert.False(state.IsRefreshing));
            Assert.Equal(2, loader.DecodeCount);
            Assert.NotNull(loader.ReplacementBase);
            using var expected = new RenderPipeline().Render(new RenderRequest(
                loader.ReplacementBase!,
                latestSettings,
                RenderIntent.Preview,
                BaseImage.PreviewMaxDimension,
                new RenderOptions(false, false)));
            using var expectedBitmap =
                BitmapConversionService.ConvertToBitmap(expected.Image);
            Assert.Equal(
                BitmapConversionService.CopyBgraPixels(expectedBitmap!),
                BitmapConversionService.CopyBgraPixels(refreshed));

            initial!.Dispose();
            latestResult.preview!.Dispose();
        }
    }

    private async Task<(CatalogService Catalog, ImageFile File)>
        CreateCatalogAndFile()
    {
        Directory.CreateDirectory(_tempDirectory);
        var source = Path.Combine(
            _tempDirectory,
            $"{Guid.NewGuid():N}.jpg");
        await File.WriteAllBytesAsync(source, [1]);
        var catalog = new CatalogService(Path.Combine(
            _tempDirectory,
            $"catalog-{Guid.NewGuid():N}"));
        await catalog.InitializeAsync();
        return (catalog, new ImageFile(source));
    }

    private static PreviewService CreateService(
        CatalogService catalog,
        IBaseImageLoader loader,
        Action renderStarted)
    {
        var service = new PreviewService(
            catalog,
            loader,
            new RenderPipeline(),
            new HistogramService(),
            new PreviewCacheService(catalog),
            new RenderedThumbnailCacheService(catalog));
        service.RenderStarted += renderStarted;
        return service;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private sealed class BlockingLoader : IBaseImageLoader
    {
        private int _decodeCount;

        public ManualResetEventSlim Started { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public int DecodeCount => Volatile.Read(ref _decodeCount);

        public bool CanLoad(ImageFile file) => true;

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _decodeCount);
            Started.Set();
            Release.Wait(cancellationToken);
            return new BaseImage(
                new MagickImage(MagickColors.Gray, 32, 24)
                {
                    Depth = 16,
                    ColorSpace = ColorSpace.RGB
                },
                new BaseImageInfo(
                    BaseSourceKind.Standard,
                    false,
                    decode,
                    null,
                    null,
                    6504,
                    0,
                    false,
                    null,
                    1,
                    32,
                    24));
        }

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class DecodeTransitionLoader : IBaseImageLoader
    {
        private int _decodeCount;

        public ManualResetEventSlim ReplacementStarted { get; } = new();
        public ManualResetEventSlim ReleaseReplacement { get; } = new();
        public int DecodeCount => Volatile.Read(ref _decodeCount);
        public BaseImage? ReplacementBase { get; private set; }

        public bool CanLoad(ImageFile file) => true;

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _decodeCount);
            if (call == 2)
            {
                ReplacementStarted.Set();
                ReleaseReplacement.Wait(cancellationToken);
            }

            var result = new BaseImage(
                new MagickImage(
                    call == 1 ? MagickColors.Red : MagickColors.Blue,
                    32,
                    24)
                {
                    Depth = 16,
                    ColorSpace = ColorSpace.RGB
                },
                new BaseImageInfo(
                    BaseSourceKind.Standard,
                    false,
                    decode,
                    null,
                    null,
                    6504,
                    0,
                    false,
                    null,
                    1,
                    32,
                    24));
            if (call == 2)
            {
                ReplacementBase = result;
            }
            return result;
        }

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
