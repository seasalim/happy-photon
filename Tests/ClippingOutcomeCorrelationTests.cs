using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ClippingOutcomeCorrelationTests : IDisposable
{
    private readonly CatalogVmFixture _fx = new("clipping-correlation");
    private readonly List<CatalogService> _catalogs = [];

    [AvaloniaFact]
    public async Task ClippingUpgradeFromRolledBackSettingsMatchesPaintedSurface()
    {
        var vm = await CreateDevelopViewModelAsync("rollback");
        var editAtGate = NewSignal();
        var releaseEdit = NewSignal();
        var overlayAtGate = NewSignal();
        var releaseOverlay = NewSignal();
        var gateCalls = 0;

        try
        {
            vm.ToggleClippingOverlayCommand.Execute(null);
            Assert.True(vm.IsClippingOverlayLatched);
            await TestWaits.UntilAsync(() => vm.PreviewClippingMask != null);
            var painted = vm.PreviewImage!;

            // Hold the rotated state-defining render at its gate, then fail
            // it there; hold the rotated overlay-only render behind it.
            vm.ImageService.Previews.RenderGateAsync = () =>
            {
                var call = Interlocked.Increment(ref gateCalls);
                if (call == 1)
                {
                    editAtGate.TrySetResult();
                    return releaseEdit.Task;
                }
                if (call == 2)
                {
                    overlayAtGate.TrySetResult();
                    return releaseOverlay.Task;
                }
                return Task.CompletedTask;
            };

            vm.RotateRightCommand.Execute(null);
            await editAtGate.Task.WaitAsync(TestWaits.Condition);

            // Re-latch while the rotated render is pending: the overlay-only
            // render captures the pending (rotated) settings and the shared
            // surface generation.
            vm.ToggleClippingOverlayCommand.Execute(null);
            vm.ToggleClippingOverlayCommand.Execute(null);
            Assert.True(vm.IsClippingOverlayLatched);
            await overlayAtGate.Task.WaitAsync(TestWaits.Condition);

            // The state-defining render fails and rolls the edit back; the
            // painted surface and settings return to the unrotated state.
            releaseEdit.TrySetException(
                new InvalidOperationException("render failed"));
            await TestWaits.UntilAsync(() => vm.Rotation == 0);

            var rendersCompleted = 0;
            vm.ImageService.Previews.RenderRequestCompleted += _ =>
                Interlocked.Increment(ref rendersCompleted);
            releaseOverlay.TrySetResult();
            await TestWaits.UntilAsync(
                () => Volatile.Read(ref rendersCompleted) >= 1);
            await Dispatcher.UIThread.InvokeAsync(() => { });

            Assert.Same(painted, vm.PreviewImage);
            Assert.Equal(0, vm.SelectedImage!.EditSettings.Rotation);
            // A mask from the rolled-back rotated settings must not stand over
            // the unrotated painted surface; no mask at all is acceptable.
            if (vm.PreviewClippingMask is { } mask)
            {
                Assert.Equal(painted.PixelSize.Width, mask.Width);
                Assert.Equal(painted.PixelSize.Height, mask.Height);
            }
        }
        finally
        {
            releaseEdit.TrySetResult();
            releaseOverlay.TrySetResult();
            vm.ImageService.Previews.RenderGateAsync = null;
            await vm.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task FailedOverlayRenderPreservesPaintedScopes()
    {
        var vm = await CreateDevelopViewModelAsync("failed-overlay");

        try
        {
            vm.ToggleClippingOverlayCommand.Execute(null);
            Assert.True(vm.IsClippingOverlayLatched);
            await TestWaits.UntilAsync(() => vm.PreviewClippingMask != null);
            var painted = vm.PreviewImage!;
            var paintedStats = vm.DisplayClippingStats;
            Assert.NotNull(paintedStats);

            // The re-latched overlay-only render fails; nothing was painted,
            // so the stats describing the painted surface must survive.
            var rendersCompleted = 0;
            vm.ImageService.Previews.RenderRequestCompleted += _ =>
                Interlocked.Increment(ref rendersCompleted);
            vm.ImageService.Previews.RenderGateAsync = () =>
                Task.FromException(
                    new InvalidOperationException("render failed"));
            vm.ToggleClippingOverlayCommand.Execute(null);
            vm.ToggleClippingOverlayCommand.Execute(null);
            Assert.True(vm.IsClippingOverlayLatched);
            await TestWaits.UntilAsync(
                () => Volatile.Read(ref rendersCompleted) >= 1);
            await Dispatcher.UIThread.InvokeAsync(() => { });

            Assert.Same(painted, vm.PreviewImage);
            Assert.Same(paintedStats, vm.DisplayClippingStats);
        }
        finally
        {
            vm.ImageService.Previews.RenderGateAsync = null;
            await vm.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task OriginalSurfaceOverlayDescribesOriginalNotEdited()
    {
        var catalog = await _fx.CreateCatalogAsync("original-overlay");
        _catalogs.Add(catalog);
        var loader = new HlToneLoader();
        var vm = _fx.CreateViewModel(
            catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        vm.ShowWorkspaceReady(
            MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var curve = new CurveData();
        curve.MovePoint(1, 1, 0);
        vm.SelectedImage = new ImageFile(_fx.Path("original-overlay.dng"))
        {
            EditSettings = new EditSettings
            {
                Curve = curve,
                HlReconstruction = HlReconstructionMode.Blend
            }
        };

        try
        {
            await TestWaits.UntilAsync(() => vm.PreviewImage != null);
            // The edited curve maps the white Blend base to black.
            await TestWaits.UntilAsync(
                () => vm.DisplayClippingStats is { LowAll: > 0.5 });
            vm.ToggleClippingOverlayCommand.Execute(null);
            await TestWaits.UntilAsync(() => vm.PreviewClippingMask != null);

            // Before/After reverts the curve but keeps the Blend decode family.
            await vm.ToggleBeforeAfterCommand.ExecuteAsync(null);
            await TestWaits.UntilAsync(() => vm.IsShowingOriginal);
            await TestWaits.UntilAsync(
                () => vm.DisplayClippingStats is { LowAll: < 0.5 });

            // Force a standalone overlay render while the original is shown.
            var renders = 0;
            vm.ImageService.Previews.RenderRequestCompleted += _ =>
                Interlocked.Increment(ref renders);
            vm.ToggleClippingOverlayCommand.Execute(null);
            vm.ToggleClippingOverlayCommand.Execute(null);
            await TestWaits.UntilAsync(() => Volatile.Read(ref renders) >= 1);
            await TestWaits.UntilAsync(() => vm.PreviewClippingMask != null);
            await Dispatcher.UIThread.InvokeAsync(() => { });

            // The overlay describes the painted original (white), not the
            // curve-darkened edited surface, without changing decode family.
            Assert.True(vm.DisplayClippingStats!.LowAll < 0.5);
            Assert.All(loader.Decodes, decode => Assert.Equal(
                HlReconstructionMode.Blend,
                decode.HlReconstruction));
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }

    public void Dispose()
    {
        foreach (var catalog in _catalogs)
        {
            catalog.Dispose();
        }
        _fx.Dispose();
    }

    private async Task<MainWindowViewModel> CreateDevelopViewModelAsync(
        string subdirectory)
    {
        var catalog = await _fx.CreateCatalogAsync(subdirectory);
        _catalogs.Add(catalog);
        var vm = _fx.CreateViewModel(
            catalog,
            new WhiteLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        vm.ShowWorkspaceReady(
            MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.SelectedImage = new ImageFile(_fx.Path($"{subdirectory}.jpg"));
        await TestWaits.UntilAsync(() => vm.PreviewImage != null);
        return vm;
    }

    private static TaskCompletionSource NewSignal() => new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class WhiteLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        public BaseImage LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            new(
                new MagickImage(MagickColors.White, 64, 48),
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

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            var sourceSaturation = new SourceSaturationMask(64, 48);
            for (var y = 0; y < sourceSaturation.Height; y++)
            for (var x = 0; x < sourceSaturation.Width; x++)
            {
                sourceSaturation.SetFlags(x, y, 7);
            }
            return BaseImageLoadOutcome.Loaded(
                new PreviewBasePair(
                    LoadPreviewBase(file, decode, cancellationToken),
                    large: null),
                new PreviewSourceAnalysis(null, sourceSaturation));
        }

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class HlToneLoader : IBaseImageLoader
    {
        public List<BaseDecodeSettings> Decodes { get; } = [];
        public bool CanLoad(ImageFile file) => true;

        public BaseImage LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            Decodes.Add(decode);
            return new(
                new MagickImage(MagickColors.White,
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
        }

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
}
