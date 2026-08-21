using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PreviewCacheOutcomeOrderingTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-cache-order-{Guid.NewGuid():N}")).FullName;

    [AvaloniaTheory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task CacheAndFreshCompletionOrdersRespectSuccessfulPaintStage(
        bool cacheFirst,
        bool freshSucceeds)
    {
        using var catalog = new CatalogService(Path.Combine(
            _root,
            $"{cacheFirst}-{freshSucceeds}"));
        await catalog.InitializeAsync();
        var path = Path.Combine(
            _root,
            $"source-{cacheFirst}-{freshSucceeds}.jpg");
        using (var source = new MagickImage(MagickColors.Gray, 64, 48))
        {
            source.Write(path);
        }
        var image = new ImageFile(path);
        await image.EnsureCatalogIdAsync(catalog);
        await SeedCacheAsync(catalog, image);
        var loader = new GatedLoader(freshSucceeds);
        var vm = CreateViewModel(catalog, loader);
        var releaseCache = NewSignal();
        var releaseRender = NewSignal();
        if (cacheFirst)
        {
            vm.ImageService.Previews.RenderGateAsync = () => releaseRender.Task;
        }
        else
        {
            vm.ImageService.Previews.CachedPreviewGateAsync = () =>
                releaseCache.Task;
        }

        try
        {
            vm.SelectedImage = image;
            if (cacheFirst)
            {
                await TestWaits.UntilAsync(() => IsRed(vm.PreviewImage));
                Assert.Null(vm.Histogram);
                releaseRender.TrySetResult();
                if (freshSucceeds)
                {
                    await TestWaits.UntilAsync(() => IsBlue(vm.PreviewImage));
                    Assert.NotNull(vm.Histogram);
                }
                else
                {
                    await loader.Completed.Task.WaitAsync(TestWaits.Condition);
                    Assert.True(IsRed(vm.PreviewImage));
                    Assert.Null(vm.Histogram);
                }
            }
            else
            {
                await loader.Completed.Task.WaitAsync(TestWaits.Condition);
                if (freshSucceeds)
                {
                    await TestWaits.UntilAsync(() => IsBlue(vm.PreviewImage));
                    Assert.NotNull(vm.Histogram);
                }
                else
                {
                    Assert.Null(vm.PreviewImage);
                    Assert.Null(vm.Histogram);
                }
                releaseCache.TrySetResult();
                if (freshSucceeds)
                {
                    await Task.Yield();
                    Assert.True(IsBlue(vm.PreviewImage));
                    Assert.NotNull(vm.Histogram);
                }
                else
                {
                    await TestWaits.UntilAsync(() => IsRed(vm.PreviewImage));
                    Assert.Null(vm.Histogram);
                }
            }
        }
        finally
        {
            releaseCache.TrySetResult();
            releaseRender.TrySetResult();
            await vm.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task ReplacementRefreshClosesStageBeforeLateStaleBasePaint()
    {
        using var catalog = new CatalogService(Path.Combine(_root, "refresh"));
        await catalog.InitializeAsync();
        var vm = CreateViewModel(catalog, new RedThenBlueRawLoader());
        vm.SelectedImage = new ImageFile(Path.Combine(_root, "refresh.dng"));
        var staleStarted = NewSignal();
        var releaseStale = NewSignal();

        try
        {
            await TestWaits.UntilAsync(() => IsRed(vm.PreviewImage));
            vm.ImageService.Previews.RenderGateAsync = () =>
            {
                staleStarted.TrySetResult();
                return releaseStale.Task;
            };

            vm.HlReconstruction = HlReconstructionMode.Blend;
            await staleStarted.Task.WaitAsync(TestWaits.Condition);
            await TestWaits.UntilAsync(() => IsBlue(vm.PreviewImage));
            var refreshed = vm.PreviewImage;
            var refreshedHistogram = vm.Histogram;
            var staleConverted = NewSignal();
            vm.ImageService.Previews.PreviewConverted += () =>
                staleConverted.TrySetResult();

            releaseStale.TrySetResult();
            await staleConverted.Task.WaitAsync(TestWaits.Condition);

            Assert.Same(refreshed, vm.PreviewImage);
            Assert.Same(refreshedHistogram, vm.Histogram);
        }
        finally
        {
            releaseStale.TrySetResult();
            await vm.DisposeAsync();
        }
    }

    private static async Task SeedCacheAsync(
        CatalogService catalog,
        ImageFile image)
    {
        await using var cache = new PreviewCacheService(catalog);
        using var cached = new MagickImage(MagickColors.Red, 64, 48);
        cache.QueueSaveToCache(
            image,
            cached,
            RenderSettingsHash.Compute(image.EditSettings));
    }

    private static bool IsRed(Avalonia.Media.Imaging.Bitmap? bitmap) =>
        DominantChannel(bitmap) > 0;

    private static bool IsBlue(Avalonia.Media.Imaging.Bitmap? bitmap) =>
        DominantChannel(bitmap) < 0;

    private static int DominantChannel(Avalonia.Media.Imaging.Bitmap? bitmap)
    {
        if (bitmap == null)
        {
            return 0;
        }
        var pixels = BitmapConversionService.CopyBgraPixels(bitmap);
        return pixels[2] - pixels[0];
    }

    private static MainWindowViewModel CreateViewModel(
        CatalogService catalog,
        IBaseImageLoader loader) =>
        new(
            catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally))
        {
            IsDevelopMode = true
        };

    private static TaskCompletionSource NewSignal() => new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class GatedLoader(bool succeeds) : IBaseImageLoader
    {
        public TaskCompletionSource Completed { get; } = NewSignal();

        public bool CanLoad(ImageFile file) => true;

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            try
            {
                return succeeds
                    ? new BaseImage(
                        new MagickImage(MagickColors.Blue, 64, 48),
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
                            64,
                            48))
                    : null;
            }
            finally
            {
                Completed.TrySetResult();
            }
        }

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.FromImage(
                LoadPreviewBase(file, decode, cancellationToken),
                BaseImageLoadFailure.DecodeFailed);

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RedThenBlueRawLoader : IBaseImageLoader
    {
        private int _loads;

        public bool CanLoad(ImageFile file) => true;

        public BaseImage LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            new(
                new MagickImage(
                    Interlocked.Increment(ref _loads) == 1
                        ? MagickColors.Red
                        : MagickColors.Blue,
                    64,
                    48),
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

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.Loaded(LoadPreviewBase(
                file,
                decode,
                cancellationToken));

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }
}
