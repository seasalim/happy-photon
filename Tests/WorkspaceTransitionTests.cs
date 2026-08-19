using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WorkspaceTransitionTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-workspace-transition-{Guid.NewGuid():N}")).FullName;

    [AvaloniaFact]
    public async Task ReplacementRefresh_CrossingIntoLibraryIsRejected()
    {
        using var catalog = new CatalogService(_root);
        await catalog.InitializeAsync();
        var loader = new DecodeTransitionLoader();
        var vm = CreateViewModel(catalog, loader);
        var refreshReady = NewSignal();
        var releaseRefresh = NewSignal();
        vm.PreviewRefreshReadyGateAsync = () =>
        {
            refreshReady.TrySetResult();
            return releaseRefresh.Task;
        };
        var path = Path.Combine(_root, "replacement.png");
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
        vm.Library.SetImages([image]);
        vm.SelectedImage = image;

        try
        {
            await TestWaits.UntilAsync(() =>
                vm.PreviewImage != null && vm.Histogram != null);

            var toggle = vm.ToggleBeforeAfterCommand.ExecuteAsync(null);
            await toggle;
            Assert.True(vm.IsShowingOriginal);
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
            vm.Library.ReplaceThumbnail(image, null);
            await vm.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task BeforeAfterRender_CrossingIntoLibraryIsRejected()
    {
        using var catalog = new CatalogService(_root);
        await catalog.InitializeAsync();
        var vm = CreateViewModel(catalog, new SolidLoader());
        var image = new ImageFile(Path.Combine(_root, "original.png"))
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
            vm.PreviewRenderGateAsync = () =>
            {
                renderStarted.TrySetResult();
                return releaseRender.Task;
            };

            var toggle = vm.ToggleBeforeAfterCommand.ExecuteAsync(null);
            await renderStarted.Task.WaitAsync(TestWaits.Condition);
            Assert.True(vm.IsShowingOriginal);

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
        using var catalog = new CatalogService(_root);
        await catalog.InitializeAsync();
        var vm = CreateViewModel(catalog, new SolidLoader());
        var image = new ImageFile(Path.Combine(_root, "before-after.png"))
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
            vm.PreviewRenderGateAsync = () =>
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
        using var catalog = new CatalogService(_root);
        await catalog.InitializeAsync();
        var vm = CreateViewModel(catalog, new SolidLoader());
        var image = new ImageFile(Path.Combine(_root, "shared.png"))
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
            vm.PreviewRenderGateAsync = () =>
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

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
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

    private sealed class DecodeTransitionLoader : IBaseImageLoader
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

    private sealed class SolidLoader : IBaseImageLoader
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
