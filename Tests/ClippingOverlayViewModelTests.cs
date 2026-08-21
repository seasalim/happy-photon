using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ClippingOverlayViewModelTests : IDisposable
{
    private readonly CatalogVmFixture _fx = new("clipping-vm");

    [Fact]
    public async Task Shortcut_IsDevelopOnlyAndLatchIsSessionState()
    {
        using var catalog = _fx.CreateCatalog("catalog");
        await using var vm = _fx.CreateViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        vm.SelectedImage = new ImageFile(_fx.Path("photo.jpg"));

        Assert.False(vm.ToggleClippingOverlayCommand.CanExecute(null));
        vm.ShowWorkspaceReady(
            MainWindowViewModel.CurrentFirstRunExperienceVersion);
        Assert.False(vm.ToggleClippingOverlayCommand.CanExecute(null));

        vm.IsDevelopMode = true;
        Assert.True(vm.ToggleClippingOverlayCommand.CanExecute(null));
        vm.ToggleClippingOverlayCommand.Execute(null);

        Assert.True(vm.IsClippingOverlayLatched);
        Assert.Equal("Clipping indicators on", vm.AssessmentFeedback);
        Assert.Equal(
            ClippingOverlaySide.Both,
            vm.RequestedClippingOverlaySides);

        vm.IsFullScreenMode = true;
        Assert.False(vm.ToggleClippingOverlayCommand.CanExecute(null));
        Assert.Equal(
            ClippingOverlaySide.None,
            vm.RequestedClippingOverlaySides);
        Assert.True(vm.IsClippingOverlayLatched);

        vm.IsFullScreenMode = false;
        Assert.True(vm.ToggleClippingOverlayCommand.CanExecute(null));
        vm.ToggleClippingOverlayCommand.Execute(null);
        Assert.False(vm.IsClippingOverlayLatched);
        Assert.Equal("Clipping indicators off", vm.AssessmentFeedback);

        vm.IsDevelopMode = false;
        Assert.False(vm.ToggleClippingOverlayCommand.CanExecute(null));
        Assert.False(vm.IsClippingOverlayLatched);
    }

    [AvaloniaFact]
    public async Task LatchAndPeekTransitionsPreserveOrClearOnlyAsRequired()
    {
        using var catalog = await _fx.CreateCatalogAsync("masks");
        await using var vm = _fx.CreateViewModel(
            catalog,
            new WhiteLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        vm.ShowWorkspaceReady(
            MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.SelectedImage = new ImageFile(_fx.Path("photo.jpg"));
        await TestWaits.UntilAsync(() => vm.PreviewImage != null);

        vm.ToggleClippingOverlayCommand.Execute(null);
        await TestWaits.UntilAsync(() => vm.PreviewClippingMask != null);
        var latchedMask = vm.PreviewClippingMask;
        // Standard-source latch and peek both retain the highlight side.
        Assert.Equal(ClippingOverlaySide.Both, latchedMask!.Sides);
        Assert.All(latchedMask.Flags.ToArray(), flag => Assert.Equal(
            (byte)ClippingOverlaySide.Highlights,
            flag));

        vm.BeginClippingPeek(ClippingOverlaySide.DisplayFloor);
        vm.EndClippingPeek();
        Assert.Same(latchedMask, vm.PreviewClippingMask);

        vm.ToggleClippingOverlayCommand.Execute(null);
        Assert.Null(vm.PreviewClippingMask);

        vm.BeginClippingPeek(ClippingOverlaySide.DisplayFloor);
        await TestWaits.UntilAsync(() => vm.PreviewClippingMask != null);
        vm.ToggleClippingOverlayCommand.Execute(null);
        Assert.NotNull(vm.PreviewClippingMask);
        vm.ToggleClippingOverlayCommand.Execute(null);
        Assert.NotNull(vm.PreviewClippingMask);

        vm.EndClippingPeek();
        Assert.Null(vm.PreviewClippingMask);

        vm.BeginClippingPeek(ClippingOverlaySide.Highlights);
        await TestWaits.UntilAsync(() => vm.PreviewClippingMask != null);
        Assert.Equal(
            ClippingOverlaySide.Highlights,
            vm.PreviewClippingMask!.Sides);
        Assert.All(vm.PreviewClippingMask.Flags.ToArray(), flag => Assert.Equal(
            (byte)ClippingOverlaySide.Highlights,
            flag));
        vm.EndClippingPeek();
    }

    [AvaloniaFact]
    public async Task MissingSourceArtifactDisablesOnlyHighlightInteractions()
    {
        using var catalog = await _fx.CreateCatalogAsync("floor-only");
        await using var vm = _fx.CreateViewModel(
            catalog,
            new WhiteLoader(includeSourceSaturation: false, black: true),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        vm.ShowWorkspaceReady(
            MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.SelectedImage = new ImageFile(_fx.Path("photo.png"));
        await TestWaits.UntilAsync(() => vm.DisplayClippingStats != null);

        Assert.False(vm.IsHighlightClippingAvailable);
        vm.ToggleClippingOverlayCommand.Execute(null);
        await TestWaits.UntilAsync(() => vm.PreviewClippingMask != null);
        Assert.Equal(
            ClippingOverlaySide.DisplayFloor,
            vm.PreviewClippingMask!.Sides);

        vm.ToggleClippingOverlayCommand.Execute(null);
        vm.BeginClippingPeek(ClippingOverlaySide.Highlights);
        Assert.Equal(ClippingOverlaySide.None, vm.VisibleClippingOverlaySides);
        Assert.Null(vm.PreviewClippingMask);

        vm.BeginClippingPeek(ClippingOverlaySide.DisplayFloor);
        await TestWaits.UntilAsync(() => vm.PreviewClippingMask != null);
        Assert.Equal(
            ClippingOverlaySide.DisplayFloor,
            vm.PreviewClippingMask!.Sides);
        vm.EndClippingPeek();
    }

    public void Dispose() => _fx.Dispose();

    private sealed class WhiteLoader : IBaseImageLoader
    {
        private readonly bool _includeSourceSaturation;
        private readonly bool _black;

        internal WhiteLoader(
            bool includeSourceSaturation = true,
            bool black = false)
        {
            _includeSourceSaturation = includeSourceSaturation;
            _black = black;
        }

        public bool CanLoad(ImageFile file) => true;

        public BaseImage LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            SourceSaturationMask? sourceSaturation = null;
            if (_includeSourceSaturation)
            {
                sourceSaturation = new SourceSaturationMask(64, 48);
                for (var y = 0; y < sourceSaturation.Height; y++)
                for (var x = 0; x < sourceSaturation.Width; x++)
                {
                    sourceSaturation.SetFlags(x, y, 7);
                }
            }
            return new BaseImage(
                new MagickImage(
                    _black ? MagickColors.Black : MagickColors.White,
                    64,
                    48),
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
                    48),
                sourceSaturation);
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
