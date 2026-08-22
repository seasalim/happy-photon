using Avalonia.Headless.XUnit;
using Avalonia.Threading;
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
    [InlineData(true, true, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, false, false)]
    public async Task CacheAndFreshCompletionOrdersRespectSuccessfulPaintStage(
        bool cacheFirst,
        bool freshSucceeds,
        bool settingsMatch)
    {
        using var catalog = new CatalogService(Path.Combine(
            _root,
            $"{cacheFirst}-{freshSucceeds}-{settingsMatch}"));
        await catalog.InitializeAsync();
        var path = Path.Combine(
            _root,
            $"source-{cacheFirst}-{freshSucceeds}-{settingsMatch}.jpg");
        using (var source = new MagickImage(MagickColors.Gray, 64, 48))
        {
            source.Write(path);
        }
        var image = new ImageFile(path);
        await image.EnsureCatalogIdAsync(catalog);
        await SeedCacheAsync(catalog, image);
        if (!settingsMatch)
        {
            image.EditSettings = new EditSettings { Exposure = 1 };
        }
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
                AssertCachedScopes(vm, settingsMatch);
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
                    AssertCachedScopes(vm, settingsMatch);
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
                    AssertCachedScopes(vm, settingsMatch);
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

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MatchingCachePublishesScopesBeforeAnySourceWork(
        bool embeddedProfile)
    {
        using var catalog = new CatalogService(Path.Combine(
            _root,
            $"source-gate-{embeddedProfile}"));
        await catalog.InitializeAsync();
        var path = Path.Combine(
            _root,
            embeddedProfile ? "source-gate.dng" : "source-gate.jpg");
        using (var source = new MagickImage(MagickColors.Gray, 64, 48))
        {
            source.Write(path, MagickFormat.Jpeg);
        }
        var image = new ImageFile(path);
        if (embeddedProfile)
        {
            image.EditSettings = new EditSettings
            {
                RawProfile = new RawProfileSelection
                {
                    Source = RawProfileSource.Embedded,
                    ContentHash = new string('a', 64)
                }
            };
        }
        await image.EnsureCatalogIdAsync(catalog);
        await SeedCacheAsync(catalog, image);
        var loader = new CountingLoader();
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.AvailableLocally);
        var vm = CreateViewModel(catalog, loader, availability);
        var releaseSource = NewSignal();
        var renderCount = 0;
        vm.ImageService.Previews.SourceWorkGateAsync = () => releaseSource.Task;
        vm.ImageService.Previews.RenderStarted += () => renderCount++;

        try
        {
            vm.SelectedImage = image;
            await TestWaits.UntilAsync(() => IsRed(vm.PreviewImage));

            AssertCachedScopes(vm, settingsMatch: true);
            Assert.Equal(0, loader.LoadCount);
            Assert.Equal(0, renderCount);
            Assert.Equal(1, availability.CallCount);
        }
        finally
        {
            releaseSource.TrySetResult();
            await vm.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task CloudAvailabilityTransitionKeepsMatchingCachedScopes()
    {
        using var catalog = new CatalogService(Path.Combine(_root, "cloud"));
        await catalog.InitializeAsync();
        var path = Path.Combine(_root, "cloud.dng");
        using (var source = new MagickImage(MagickColors.Gray, 64, 48))
        {
            source.Write(path, MagickFormat.Jpeg);
        }
        var image = new ImageFile(
            path,
            SourceAvailability.AvailableLocally)
        {
            EditSettings = new EditSettings
            {
                RawProfile = new RawProfileSelection
                {
                    Source = RawProfileSource.Embedded,
                    ContentHash = new string('b', 64)
                }
            }
        };
        await image.EnsureCatalogIdAsync(catalog);
        await SeedCacheAsync(catalog, image);
        var loader = new CountingLoader();
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.RequiresHydration);
        var vm = CreateViewModel(catalog, loader, availability);
        var renderCount = 0;
        var sourceSettled = NewSignal();
        vm.ImageService.Previews.RenderStarted += () => renderCount++;
        vm.ImageService.Previews.PreviewLoadCompleted += (_, outcome) =>
        {
            if (!outcome.Succeeded)
            {
                sourceSettled.TrySetResult();
            }
        };

        try
        {
            vm.SelectedImage = image;
            await TestWaits.UntilAsync(() => IsRed(vm.PreviewImage));
            await TestWaits.UntilAsync(() => image.SourceRequiresHydration);
            await sourceSettled.Task.WaitAsync(TestWaits.Condition);
            await Dispatcher.UIThread.InvokeAsync(() => { });

            AssertCachedScopes(vm, settingsMatch: true);
            Assert.Equal(0, loader.LoadCount);
            Assert.Equal(0, renderCount);
        }
        finally
        {
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

    private static void AssertCachedScopes(
        MainWindowViewModel vm,
        bool settingsMatch)
    {
        if (!settingsMatch)
        {
            Assert.Null(vm.Histogram);
            Assert.Null(vm.DisplayClippingStats);
            return;
        }

        Assert.NotNull(vm.Histogram);
        Assert.NotNull(vm.Histogram!.Waveform);
        Assert.NotNull(vm.DisplayClippingStats);
        Assert.False(vm.DisplayClippingStats!.IsHighAvailable);
    }

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
        IBaseImageLoader loader,
        ISourceAvailabilityService? availability = null) =>
        new(
            catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: availability ??
                new TestSourceAvailabilityService(
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

    private sealed class CountingLoader : IBaseImageLoader
    {
        private int _loadCount;

        public int LoadCount => Volatile.Read(ref _loadCount);
        public bool CanLoad(ImageFile file) => true;

        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _loadCount);
            return BaseImageLoadOutcome.Loaded(new BaseImage(
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
                    48)));
        }

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
