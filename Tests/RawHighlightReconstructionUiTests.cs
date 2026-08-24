using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RawHighlightReconstructionUiTests : IDisposable
{
    private readonly CatalogVmFixture _fx = new("highlight-ui");

    [Fact]
    public async Task Selection_PersistsResetsAndUndoesAsOneEdit()
    {
        using var catalog = await _fx.CreateCatalogAsync();
        var vm = CreateViewModel(catalog);
        vm.IsDevelopMode = true;
        var image = new ImageFile(_fx.Path("missing.dng"));
        vm.SelectedImage = image;

        vm.HlReconstruction = HlReconstructionMode.Blend;
        await TestWaits.UntilAsync(
            () => image.EditSettings.HlReconstruction ==
                  HlReconstructionMode.Blend);

        Assert.True(vm.CanReset);
        Assert.True(vm.CanUndo);

        await vm.ResetEditsCommand.ExecuteAsync(null);
        Assert.Equal(
            HlReconstructionMode.Clip,
            image.EditSettings.HlReconstruction);
        Assert.Equal(HlReconstructionMode.Clip, vm.HlReconstruction);

        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(
            HlReconstructionMode.Blend,
            image.EditSettings.HlReconstruction);
        Assert.Equal(HlReconstructionMode.Blend, vm.HlReconstruction);

        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task UnsupportedRaw_ShowsActionablePersistentStatus()
    {
        using var catalog = await _fx.CreateCatalogAsync();
        var loader = new BaseLoaderRouter(
            new RawBaseLoader(isAvailable: false),
            new StandardBaseLoader(
                (_, _) => new MagickImage(MagickColors.Gray, 64, 48)));
        var vm = _fx.CreateViewModel(
            catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        var image = new ImageFile(_fx.Path("fallback.dng"))
        {
            EditSettings = new EditSettings
            {
                HlReconstruction = HlReconstructionMode.Blend
            }
        };
        vm.SelectedImage = image;

        Assert.True(vm.CanReset);
        await TestWaits.UntilAsync(() => image.RawDecodeFailed);

        Assert.True(vm.CanReset);
        Assert.Contains("could not be decoded", vm.StatusMessage);
        Assert.Null(vm.PreviewImage);

        await vm.ResetEditsCommand.ExecuteAsync(null);
        Assert.Equal(
            HlReconstructionMode.Clip,
            image.EditSettings.HlReconstruction);
        Assert.False(vm.CanReset);

        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task ExtractedPanel_ForwardsToneCurveChanges()
    {
        using var catalog = _fx.CreateCatalog();
        catalog.InitializeAsync().GetAwaiter().GetResult();
        var vm = CreateViewModel(catalog);
        var image = new ImageFile(_fx.Path("missing.jpg"));
        vm.SelectedImage = image;
        var panel = new DevelopEditPanel
        {
            DataContext = vm
        };
        panel.Measure(new Size(250, 660));
        panel.Arrange(new Rect(0, 0, 250, 660));

        vm.OnCurveEditStarted();
        vm.CurrentCurve!.AddPointAndReturnIndex(0.5, 0.75);
        await vm.OnCurveChangedAsync();
        var curve = panel.FindControl<CurveView>("ToneCurveView")!;
        curve.Curve = vm.CurrentCurve;
        var curveChanged = false;
        curve.CurveChanged += (_, _) => curveChanged = true;
        curve.ResetCurve();

        Assert.True(curveChanged);
        Assert.Same(vm, panel.DataContext);
        Assert.True(vm.CanUndo);
        Assert.True(image.EditSettings.Curve.IsIdentity());

        await Task.Delay(250);
        panel.DataContext = null;
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Brightness_GatesByProvisionalAndDecodedSourceRegime()
    {
        using var catalog = _fx.CreateCatalog();
        var vm = CreateViewModel(catalog);
        var raw = new ImageFile(_fx.Path("raw.dng"))
        {
            EditSettings = new EditSettings { Brightness = 37 }
        };
        var standard = new ImageFile(_fx.Path("standard.jpg"))
        {
            EditSettings = new EditSettings { Brightness = -23 }
        };
        var panel = new DevelopEditPanel { DataContext = vm };
        var window = new Window
        {
            Width = 250,
            Height = 660,
            Content = panel
        };
        window.Show();
        var slider = panel.FindControl<CompactSlider>("BrightnessSlider")!;

        vm.SelectedImage = raw;
        Assert.False(vm.IsBrightnessEnabled);
        Assert.False(slider.IsEnabled);
        Assert.Equal(37, vm.Brightness);
        Assert.InRange(slider.Opacity, 0, 0.99);

        vm.SelectedImage = standard;
        Assert.True(vm.IsBrightnessEnabled);
        Assert.True(slider.IsEnabled);
        Assert.Equal(-23, vm.Brightness);

        vm.SelectedImage = raw;
        Assert.False(vm.IsBrightnessEnabled);
        Assert.Equal(37, vm.Brightness);
        vm.ReconcileHighlightReconstructionCapability(raw, isRawSource: false);
        Assert.True(vm.IsBrightnessEnabled);
        Assert.Equal(37, raw.EditSettings.Brightness);
        vm.ReconcileHighlightReconstructionCapability(raw, isRawSource: true);
        Assert.False(vm.IsBrightnessEnabled);

        window.Close();
        panel.DataContext = null;
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task Selection_ClearsBeforeAfterState()
    {
        using var catalog = _fx.CreateCatalog();
        var vm = CreateViewModel(catalog);
        vm.SelectedImage = new ImageFile(_fx.Path("first.jpg"));
        vm.IsShowingOriginal = true;

        vm.SelectedImage = new ImageFile(_fx.Path("second.jpg"));

        Assert.False(vm.IsShowingOriginal);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task BrowseMode_GatesBeforeAfterUndoAndRedo()
    {
        using var catalog = _fx.CreateCatalog();
        var vm = CreateViewModel(catalog);
        var image = new ImageFile(_fx.Path("browse.jpg"))
        {
            EditSettings = new EditSettings { Exposure = 1 }
        };
        vm.SelectedImage = image;
        vm.CanUndo = true;
        vm.CanRedo = true;
        var undoNotifications = 0;
        var redoNotifications = 0;
        var beforeNotifications = 0;
        vm.UndoCommand.CanExecuteChanged += (_, _) => undoNotifications++;
        vm.RedoCommand.CanExecuteChanged += (_, _) => redoNotifications++;
        vm.ToggleBeforeAfterCommand.CanExecuteChanged +=
            (_, _) => beforeNotifications++;

        Assert.False(vm.UndoCommand.CanExecute(null));
        Assert.False(vm.RedoCommand.CanExecute(null));
        Assert.False(vm.ToggleBeforeAfterCommand.CanExecute(null));
        await vm.UndoCommand.ExecuteAsync(null);
        await vm.RedoCommand.ExecuteAsync(null);
        await vm.ToggleBeforeAfterCommand.ExecuteAsync(null);
        Assert.Equal(1, image.EditSettings.Exposure);
        Assert.False(vm.IsShowingOriginal);
        Assert.Null(vm.PreviewImage);

        vm.IsDevelopMode = true;
        Assert.True(vm.UndoCommand.CanExecute(null));
        Assert.True(vm.RedoCommand.CanExecute(null));
        Assert.True(vm.ToggleBeforeAfterCommand.CanExecute(null));
        Assert.True(undoNotifications > 0);
        Assert.True(redoNotifications > 0);
        Assert.True(beforeNotifications > 0);

        vm.IsFullScreenMode = true;
        Assert.False(vm.UndoCommand.CanExecute(null));
        Assert.False(vm.RedoCommand.CanExecute(null));
        Assert.True(vm.ToggleBeforeAfterCommand.CanExecute(null));

        vm.IsShowingOriginal = true;
        vm.IsDevelopMode = false;
        vm.IsFullScreenMode = false;
        Assert.False(vm.IsShowingOriginal);
        Assert.False(vm.ToggleBeforeAfterCommand.CanExecute(null));

        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task DevelopHistogramDoesNotScheduleDuplicateRender()
    {
        using var catalog = await _fx.CreateCatalogAsync();
        var vm = _fx.CreateViewModel(
            catalog,
            CreateSyntheticLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        await vm.InitializeAsync();
        var image = new ImageFile(_fx.Path("missing.png"));
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(
            () => vm.IsWhiteBalanceReady && vm.PreviewImage != null);

        vm.Exposure = 1;
        await TestWaits.UntilAsync(() => image.EditSettings.Exposure == 1);
        await vm.ToggleBeforeAfterCommand.ExecuteAsync(null);
        Assert.True(vm.IsShowingOriginal);

        await Task.Delay(TimeSpan.FromMilliseconds(400));
        Assert.True(vm.IsShowingOriginal);
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task DevelopToBrowse_ReschedulesHistogramFromThumbnailPixels()
    {
        using var catalog = await _fx.CreateCatalogAsync();
        var vm = _fx.CreateViewModel(
            catalog,
            CreateSyntheticLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        var image = new ImageFile(_fx.Path("histogram.png"));
        using var orange = new MagickImage(MagickColors.Orange, 16, 16);
        vm.Browse.SetImages([image]);
        vm.Browse.ReplaceThumbnail(
            image,
            BitmapConversionService.ConvertToBitmap(orange));
        vm.SelectedImage = image;
        var expectedBrowse = new HistogramService().CalculateBrowseHistogram(
            image.Thumbnail!);
        await TestWaits.UntilAsync(() => HistogramsMatch(
            vm.Histogram,
            expectedBrowse));

        vm.IsDevelopMode = true;
        await TestWaits.UntilAsync(() =>
            vm.PreviewImage != null &&
            vm.Histogram != null &&
            !HistogramsMatch(vm.Histogram, expectedBrowse));

        vm.IsDevelopMode = false;
        await TestWaits.UntilAsync(() => HistogramsMatch(
            vm.Histogram,
            expectedBrowse));

        vm.Browse.ReplaceThumbnail(image, null);
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task DevelopToBrowse_WithoutThumbnailClearsRenderHistogram()
    {
        using var catalog = await _fx.CreateCatalogAsync();
        var vm = _fx.CreateViewModel(
            catalog,
            CreateSyntheticLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        vm.SelectedImage = new ImageFile(_fx.Path("missing.png"));
        await TestWaits.UntilAsync(() => vm.Histogram != null);

        vm.IsDevelopMode = false;

        Assert.Null(vm.Histogram);
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task PresetHoverAndRestore_ClearBeforeAfterState()
    {
        using var catalog = await _fx.CreateCatalogAsync();
        var vm = _fx.CreateViewModel(
            catalog,
            CreateSyntheticLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        await vm.InitializeAsync();
        vm.SelectedImage = new ImageFile(_fx.Path("missing.png"))
        {
            EditSettings = new EditSettings { Exposure = 1 }
        };
        await TestWaits.UntilAsync(
            () => vm.IsWhiteBalanceReady && vm.PreviewImage != null);
        var preset = await vm.PresetService.SaveUserPresetAsync(
            "Hover",
            new EditSettings { Contrast = 20 });

        await vm.ToggleBeforeAfterCommand.ExecuteAsync(null);
        Assert.True(vm.IsShowingOriginal);
        await vm.PreviewPresetHoverAsync(preset.Id);
        Assert.False(vm.IsShowingOriginal);

        await vm.ToggleBeforeAfterCommand.ExecuteAsync(null);
        Assert.True(vm.IsShowingOriginal);
        await vm.RestoreFromHoverAsync();
        Assert.False(vm.IsShowingOriginal);

        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task SlowInitialPreview_UsesOneStatusActivityUntilFreshTaskSettles()
    {
        using var catalog = await _fx.CreateCatalogAsync();
        var vm = _fx.CreateViewModel(
            catalog,
            CreateSyntheticLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        var sourceStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSource = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        vm.ImageService.Previews.SourceWorkGateAsync = async () =>
        {
            sourceStarted.TrySetResult();
            await releaseSource.Task;
        };

        try
        {
            vm.IsDevelopMode = true;
            vm.SelectedImage = new ImageFile(_fx.Path("slow.png"));
            await sourceStarted.Task.WaitAsync(TestWaits.Condition);

            Assert.Equal(1, vm.InitialPreviewActivityCount);
            Assert.True(vm.CaptureBackgroundActivitySnapshot().PreviewCount > 0);
            var started = DateTimeOffset.UtcNow;
            vm.PumpBackgroundActivity(started);
            vm.PumpBackgroundActivity(
                started + BackgroundActivityAggregator.ShowDelay);

            Assert.True(vm.BackgroundActivity.IsVisible);
            Assert.Equal("Preparing preview", vm.BackgroundActivity.Label);
            Assert.Equal(1, vm.BackgroundActivity.ActiveKindCount);

            releaseSource.SetResult();
            await TestWaits.UntilAsync(() => vm.PreviewImage != null);
            await TestWaits.UntilAsync(() =>
                vm.InitialPreviewActivityCount == 0 &&
                vm.CaptureBackgroundActivitySnapshot().PreviewCount == 0);

            var settled = started +
                BackgroundActivityAggregator.ShowDelay +
                TimeSpan.FromMilliseconds(1);
            vm.PumpBackgroundActivity(settled);
            vm.PumpBackgroundActivity(
                settled + BackgroundActivityAggregator.HideDelay);
            Assert.False(vm.BackgroundActivity.IsVisible);
        }
        finally
        {
            releaseSource.TrySetResult();
            await vm.DisposeAsync();
        }
    }

    public void Dispose() => _fx.Dispose();

    private static BaseLoaderRouter CreateSyntheticLoader() =>
        new(
            new RawBaseLoader(isAvailable: false),
            new StandardBaseLoader(
                (_, _) => new MagickImage(MagickColors.Gray, 64, 48)));

    private static bool HistogramsMatch(
        HistogramData? actual,
        HistogramData expected) =>
        actual != null &&
        actual.Red.SequenceEqual(expected.Red) &&
        actual.Green.SequenceEqual(expected.Green) &&
        actual.Blue.SequenceEqual(expected.Blue) &&
        actual.Luminance.SequenceEqual(expected.Luminance);

    private MainWindowViewModel CreateViewModel(CatalogService catalog) =>
        _fx.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask);
}
