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
            Assert.True(loader.Started.Wait(TestWaits.Condition));
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
            Assert.True(loader.Started.Wait(TestWaits.Condition));
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
            var loader = new GatedSecondDecodeLoader();
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
            // Hold both renders at the pre-render gate until the latest
            // request has claimed the newer render generation; otherwise the
            // first render can finish before it is superseded.
            var gateArrivals = 0;
            var bothRendersAtGate = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseRenders = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            service.RenderGateAsync = () =>
            {
                if (Interlocked.Increment(ref gateArrivals) == 2)
                {
                    bothRendersAtGate.TrySetResult();
                }
                return releaseRenders.Task;
            };
            var firstStale = service.ApplyEditsToPreviewAsync(
                file,
                decodeSettings,
                skipHistogram: true);
            Assert.True(loader.ReplacementStarted.Wait(TestWaits.Condition));
            var latestSettings = decodeSettings.Clone();
            latestSettings.Exposure = 1;
            var latestStale = service.ApplyEditsToPreviewAsync(
                file,
                latestSettings,
                skipHistogram: true);
            await bothRendersAtGate.Task.WaitAsync(TestWaits.Condition);
            service.RenderGateAsync = null;
            releaseRenders.TrySetResult();

            var firstResult = await firstStale;
            var latestResult = await latestStale;
            Assert.Null(firstResult.preview);
            Assert.NotNull(latestResult.preview);
            Assert.False(refreshReady.Task.IsCompleted);
            var started = await refreshStarted.Task.WaitAsync(TestWaits.Condition);
            await Task.Delay(200);
            Assert.False(refreshCompleted.Task.IsCompleted);

            loader.ReleaseReplacement.Set();
            using var refreshed = await refreshReady.Task.WaitAsync(TestWaits.Condition);
            var completed = await refreshCompleted.Task.WaitAsync(TestWaits.Condition);
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
                BaseImage.InteractivePreviewMaxDimension,
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

    [WindowsFact]
    public async Task SupersededRefreshRendersRemainActiveUntilEachRenderExits()
    {
        _fixture.RequireWindows();
        var (catalog, file) = await CreateCatalogAndFile();
        using (catalog)
        {
            var loader = new OverlappingRefreshLoader();
            await using var service = new PreviewService(
                catalog,
                loader,
                new RenderPipeline(),
                new HistogramService());
            var renderStarted = new[]
            {
                new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously),
                new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously)
            };
            var renderRelease = new[]
            {
                new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously),
                new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously)
            };
            var refreshRender = -1;
            service.RefreshRenderGateAsync = () =>
            {
                var index = Interlocked.Increment(ref refreshRender);
                renderStarted[index].TrySetResult();
                return renderRelease[index].Task;
            };

            var initial = await service.ApplyEditsToPreviewAsync(
                file,
                new EditSettings(),
                skipHistogram: true);
            initial.preview?.Dispose();

            var firstRefresh = service.ApplyEditsToPreviewAsync(
                file,
                new EditSettings
                {
                    HlReconstruction = HlReconstructionMode.Blend
                },
                skipHistogram: true);
            Assert.True(loader.RefreshStarted[0].Wait(TestWaits.Condition));
            loader.RefreshRelease[0].Set();
            await renderStarted[0].Task.WaitAsync(TestWaits.Condition);
            var firstStale = await firstRefresh;
            firstStale.preview?.Dispose();

            var secondRefresh = service.ApplyEditsToPreviewAsync(
                file,
                new EditSettings
                {
                    HlReconstruction = HlReconstructionMode.Blend,
                    Detail = { NoiseReduction = FbddMode.Light }
                },
                skipHistogram: true);
            Assert.True(loader.RefreshStarted[1].Wait(TestWaits.Condition));
            loader.RefreshRelease[1].Set();
            await renderStarted[1].Task.WaitAsync(TestWaits.Condition);
            var secondStale = await secondRefresh;
            secondStale.preview?.Dispose();

            Assert.True(service.PreviewActivityCount >= 2);
            renderRelease[0].SetResult();
            await TestWaits.UntilAsync(() => service.PreviewActivityCount == 1);
            renderRelease[1].SetResult();
            await TestWaits.UntilAsync(() => service.PreviewActivityCount == 0);
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

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(ImageFile file, BaseDecodeSettings decode, CancellationToken cancellationToken) => BaseImageLoadOutcome.FromImage(LoadPreviewBase(file, decode, cancellationToken), BaseImageLoadFailure.DecodeFailed);

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

    private sealed class GatedSecondDecodeLoader : IBaseImageLoader
    {
        private int _decodeCount;

        public ManualResetEventSlim ReplacementStarted { get; } = new();
        public ManualResetEventSlim ReleaseReplacement { get; } = new();
        public int DecodeCount => Volatile.Read(ref _decodeCount);
        public BaseImage? ReplacementBase { get; private set; }

        public bool CanLoad(ImageFile file) => true;

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(ImageFile file, BaseDecodeSettings decode, CancellationToken cancellationToken) => BaseImageLoadOutcome.FromImage(LoadPreviewBase(file, decode, cancellationToken), BaseImageLoadFailure.DecodeFailed);

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

    private sealed class OverlappingRefreshLoader : IBaseImageLoader
    {
        private int _loadCount;

        public ManualResetEventSlim[] RefreshStarted { get; } =
            [new(), new()];
        public ManualResetEventSlim[] RefreshRelease { get; } =
            [new(), new()];

        public bool CanLoad(ImageFile file) => true;

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(ImageFile file, BaseDecodeSettings decode, CancellationToken cancellationToken) => BaseImageLoadOutcome.FromImage(LoadPreviewBase(file, decode, cancellationToken), BaseImageLoadFailure.DecodeFailed);

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _loadCount);
            if (call > 1)
            {
                RefreshStarted[call - 2].Set();
                RefreshRelease[call - 2].Wait(cancellationToken);
            }
            return new BaseImage(
                new MagickImage(MagickColors.Gray, 32, 24),
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
}
