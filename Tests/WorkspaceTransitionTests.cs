using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WorkspaceTransitionTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();

    [AvaloniaFact]
    public async Task ReplacementRefresh_CrossingIntoBrowseIsRejected()
    {
        using var catalog = new CatalogService(_root.Path);
        await catalog.InitializeAsync();
        var loader = new RedThenBlueDecodeLoader();
        var vm = CreateViewModel(catalog, loader);
        var refreshReady = NewSignal();
        var releaseRefresh = NewSignal();
        vm.ImageService.Previews.RefreshReadyGateAsync = () =>
        {
            refreshReady.TrySetResult();
            return releaseRefresh.Task;
        };
        var path = Path.Combine(_root.Path, "replacement.png");
        using (var source = new MagickImage(MagickColors.Orange, 64, 48))
        {
            source.Write(path);
        }
        var image = new ImageFile(path)
        {
            EditSettings = new EditSettings
            {
                Exposure = 1,
                HlReconstruction = HlReconstructionMode.Blend
            },
            HasEdits = true
        };
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;

        try
        {
            await TestWaits.UntilAsync(() =>
                vm.PreviewImage != null && vm.Histogram != null);

            vm.HlReconstruction = HlReconstructionMode.Clip;
            await refreshReady.Task.WaitAsync(TestWaits.Condition);

            vm.IsDevelopMode = false;
            vm.ClearPreviewImage();
            var activityEpoch = vm.BackgroundActivityEpoch;
            Assert.Null(vm.Histogram);

            releaseRefresh.TrySetResult();
            await TestWaits.UntilAsync(() => image.Thumbnail != null);

            Assert.True(vm.BackgroundActivityEpoch > activityEpoch);
            Assert.Null(vm.PreviewImage);
            Assert.Null(vm.Histogram);
            Assert.Same(image, vm.SelectedImage);
        }
        finally
        {
            releaseRefresh.TrySetResult();
            vm.Browse.ReplaceThumbnail(image, null);
            await vm.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task BeforeAfterRender_CrossingIntoBrowseIsRejected()
    {
        using var catalog = new CatalogService(_root.Path);
        await catalog.InitializeAsync();
        var vm = CreateViewModel(catalog, new GraySolidLoader());
        var image = new ImageFile(Path.Combine(_root.Path, "original.png"))
        {
            EditSettings = new EditSettings { Exposure = 1 },
            HasEdits = true
        };
        vm.SelectedImage = image;
        var renderStarted = NewSignal();
        var releaseRender = NewSignal();

        try
        {
            await TestWaits.UntilAsync(() =>
                vm.PreviewImage != null && vm.Histogram != null);
            vm.ImageService.Previews.RenderGateAsync = () =>
            {
                renderStarted.TrySetResult();
                return releaseRender.Task;
            };

            var toggle = vm.ToggleBeforeAfterCommand.ExecuteAsync(null);
            await renderStarted.Task.WaitAsync(TestWaits.Condition);
            Assert.False(vm.IsShowingOriginal);

            vm.IsDevelopMode = false;
            vm.ClearPreviewImage();
            releaseRender.TrySetResult();
            await toggle;

            Assert.Null(vm.PreviewImage);
            Assert.False(vm.IsShowingOriginal);
        }
        finally
        {
            releaseRender.TrySetResult();
            await vm.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task BeforeAfterRender_SameImageSupersededSentinelIsRejected()
    {
        using var catalog = new CatalogService(_root.Path);
        await catalog.InitializeAsync();
        var vm = CreateViewModel(catalog, new GraySolidLoader());
        var image = new ImageFile(Path.Combine(_root.Path, "before-after.png"))
        {
            EditSettings = new EditSettings { Exposure = 1 },
            HasEdits = true
        };
        vm.SelectedImage = image;
        var started = new[] { NewSignal(), NewSignal() };
        var release = new[] { NewSignal(), NewSignal() };
        var gateIndex = -1;

        try
        {
            await TestWaits.UntilAsync(() =>
                vm.PreviewImage != null && vm.Histogram != null);
            var originalPreview = vm.PreviewImage;
            var originalHistogram = vm.Histogram;
            vm.ImageService.Previews.RenderGateAsync = () =>
            {
                var index = Interlocked.Increment(ref gateIndex);
                started[index].TrySetResult();
                return release[index].Task;
            };

            var beforeAfter = vm.ToggleBeforeAfterCommand.ExecuteAsync(null);
            await started[0].Task.WaitAsync(TestWaits.Condition);
            var reset = vm.ResetEditsCommand.ExecuteAsync(null);
            await started[1].Task.WaitAsync(TestWaits.Condition);

            release[0].TrySetResult();
            await beforeAfter;

            Assert.Same(originalPreview, vm.PreviewImage);
            Assert.Same(originalHistogram, vm.Histogram);

            release[1].TrySetResult();
            await reset;
            Assert.NotNull(vm.PreviewImage);
            Assert.NotNull(vm.Histogram);
        }
        finally
        {
            release[0].TrySetResult();
            release[1].TrySetResult();
            await vm.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task SharedRender_SameImageSupersededSentinelIsRejected()
    {
        using var catalog = new CatalogService(_root.Path);
        await catalog.InitializeAsync();
        var vm = CreateViewModel(catalog, new GraySolidLoader());
        var image = new ImageFile(Path.Combine(_root.Path, "shared.png"))
        {
            EditSettings = new EditSettings { Exposure = 1 },
            HasEdits = true
        };
        vm.SelectedImage = image;
        var started = new[] { NewSignal(), NewSignal() };
        var release = new[] { NewSignal(), NewSignal() };
        var gateIndex = -1;

        try
        {
            await TestWaits.UntilAsync(() =>
                vm.PreviewImage != null && vm.Histogram != null);
            vm.ImageService.Previews.RenderGateAsync = () =>
            {
                var index = Interlocked.Increment(ref gateIndex);
                started[index].TrySetResult();
                return release[index].Task;
            };

            var first = vm.ResetEditsCommand.ExecuteAsync(null);
            await started[0].Task.WaitAsync(TestWaits.Condition);
            var second = vm.ResetEditsCommand.ExecuteAsync(null);
            await started[1].Task.WaitAsync(TestWaits.Condition);

            release[1].TrySetResult();
            await second;
            var currentPreview = vm.PreviewImage;
            var currentHistogram = vm.Histogram;
            Assert.NotNull(currentPreview);
            Assert.NotNull(currentHistogram);

            release[0].TrySetResult();
            await first;

            Assert.Same(currentPreview, vm.PreviewImage);
            Assert.Same(currentHistogram, vm.Histogram);
        }
        finally
        {
            release[0].TrySetResult();
            release[1].TrySetResult();
            await vm.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task ImageSwitchClearsOldSurfaceUntilFirstCoherentPaint()
    {
        using var catalog = new CatalogService(_root.Path);
        await catalog.InitializeAsync();
        var vm = CreateViewModel(catalog, new PathColorLoader());
        var first = new ImageFile(Path.Combine(_root.Path, "first.png"));
        var second = new ImageFile(Path.Combine(_root.Path, "second.png"));
        var secondStarted = NewSignal();
        var releaseSecond = NewSignal();
        vm.SelectedImage = first;

        try
        {
            await TestWaits.UntilAsync(() =>
                vm.PreviewImage != null && vm.Histogram != null);
            vm.ImageService.Previews.RenderGateAsync = () =>
            {
                secondStarted.TrySetResult();
                return releaseSecond.Task;
            };

            vm.SelectedImage = second;
            Assert.Null(vm.PreviewImage);
            Assert.Null(vm.Histogram);
            Assert.False(vm.IsClippingStatsAvailable);
            await secondStarted.Task.WaitAsync(TestWaits.Condition);
            Assert.Null(vm.PreviewImage);
            Assert.Null(vm.Histogram);

            releaseSecond.TrySetResult();
            await TestWaits.UntilAsync(() =>
                vm.PreviewImage != null && vm.Histogram != null);
            Assert.Same(second, vm.SelectedImage);
            Assert.True(vm.IsClippingStatsAvailable);
        }
        finally
        {
            releaseSecond.TrySetResult();
            await vm.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task BeforeViewSurvivesLateEditedRender()
    {
        using var catalog = new CatalogService(_root.Path);
        await catalog.InitializeAsync();
        var vm = CreateViewModel(catalog, new GraySolidLoader());
        var image = new ImageFile(Path.Combine(_root.Path, "before-wins.png"))
        {
            EditSettings = new EditSettings { Exposure = 1 },
            HasEdits = true
        };
        vm.SelectedImage = image;
        var started = new[] { NewSignal(), NewSignal() };
        var release = new[] { NewSignal(), NewSignal() };
        var gateIndex = -1;

        try
        {
            await TestWaits.UntilAsync(() =>
                vm.PreviewImage != null && vm.Histogram != null);
            vm.ImageService.Previews.RenderGateAsync = () =>
            {
                var index = Interlocked.Increment(ref gateIndex);
                started[index].TrySetResult();
                return release[index].Task;
            };

            vm.Exposure = 2;
            await started[0].Task.WaitAsync(TestWaits.Condition);
            var toggle = vm.ToggleBeforeAfterCommand.ExecuteAsync(null);
            await started[1].Task.WaitAsync(TestWaits.Condition);

            release[1].TrySetResult();
            await toggle;
            Assert.True(vm.IsShowingOriginal);
            var beforeBitmap = vm.PreviewImage;
            var beforeHistogram = vm.Histogram;
            var beforeClipping = vm.DisplayClippingStats;

            var lateCompleted = NewSignal();
            vm.ImageService.Previews.RenderRequestCompleted +=
                _ => lateCompleted.TrySetResult();
            release[0].TrySetResult();
            await lateCompleted.Task.WaitAsync(TestWaits.Condition);
            Assert.True(vm.IsShowingOriginal);
            Assert.Same(beforeBitmap, vm.PreviewImage);
            Assert.Same(beforeHistogram, vm.Histogram);
            Assert.Same(beforeClipping, vm.DisplayClippingStats);
        }
        finally
        {
            release[0].TrySetResult();
            release[1].TrySetResult();
            await vm.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task DoubleBeforeToggleInvertsRequestedIntent()
    {
        using var catalog = new CatalogService(_root.Path);
        await catalog.InitializeAsync();
        var vm = CreateViewModel(catalog, new GraySolidLoader());
        vm.SelectedImage = new ImageFile(Path.Combine(_root.Path, "double-toggle.png"))
        {
            EditSettings = new EditSettings { Exposure = 1 },
            HasEdits = true
        };
        var started = new[] { NewSignal(), NewSignal() };
        var release = new[] { NewSignal(), NewSignal() };
        var gateIndex = -1;

        try
        {
            await TestWaits.UntilAsync(() => vm.PreviewImage != null);
            vm.ImageService.Previews.RenderGateAsync = () =>
            {
                var index = Interlocked.Increment(ref gateIndex);
                started[index].TrySetResult();
                return release[index].Task;
            };

            var before = vm.ToggleBeforeAfterCommand.ExecuteAsync(null);
            await started[0].Task.WaitAsync(TestWaits.Condition);
            var after = vm.ToggleBeforeAfterCommand.ExecuteAsync(null);
            await started[1].Task.WaitAsync(TestWaits.Condition);
            release[1].TrySetResult();
            await after;
            Assert.False(vm.IsShowingOriginal);

            release[0].TrySetResult();
            await before;
            Assert.False(vm.IsShowingOriginal);
        }
        finally
        {
            release[0].TrySetResult();
            release[1].TrySetResult();
            await vm.DisposeAsync();
        }
    }

    public void Dispose() => _root.Dispose();

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

    private sealed class RedThenBlueDecodeLoader : IBaseImageLoader
    {
        private int _loadCount;

        public bool CanLoad(ImageFile file) => true;

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(ImageFile file, BaseDecodeSettings decode, CancellationToken cancellationToken) => BaseImageLoadOutcome.Loaded(LoadPreviewBase(file, decode, cancellationToken));

        public BaseImage LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            var color = Interlocked.Increment(ref _loadCount) == 1
                ? MagickColors.Red
                : MagickColors.Blue;
            return CreateBase(color, decode);
        }

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class GraySolidLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(ImageFile file, BaseDecodeSettings decode, CancellationToken cancellationToken) => BaseImageLoadOutcome.Loaded(LoadPreviewBase(file, decode, cancellationToken));

        public BaseImage LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            CreateBase(MagickColors.Gray, decode);

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class PathColorLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.Loaded(LoadPreviewBase(
                file,
                decode,
                cancellationToken));

        public BaseImage LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            CreateBase(
                file.FileName.StartsWith("first", StringComparison.Ordinal)
                    ? MagickColors.Red
                    : MagickColors.Blue,
                decode);

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static BaseImage CreateBase(
        MagickColor color,
        BaseDecodeSettings decode) =>
        new(
            new MagickImage(color, 64, 48)
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
                64,
                48));
}
