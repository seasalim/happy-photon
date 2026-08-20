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
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-highlight-ui-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public async Task Selection_PersistsResetsAndUndoesAsOneEdit()
    {
        using var catalog = new CatalogService(_root);
        await catalog.InitializeAsync();
        var vm = CreateViewModel(catalog);
        vm.IsDevelopMode = true;
        var image = new ImageFile(Path.Combine(_root, "missing.dng"));
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
        using var catalog = new CatalogService(_root);
        await catalog.InitializeAsync();
        var loader = new BaseLoaderRouter(
            new RawBaseLoader(isAvailable: false),
            new StandardBaseLoader(
                (_, _) => new MagickImage(MagickColors.Gray, 64, 48)));
        var vm = new MainWindowViewModel(
            catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally))
        {
            IsDevelopMode = true
        };
        var image = new ImageFile(Path.Combine(_root, "fallback.dng"))
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
        using var catalog = new CatalogService(_root);
        catalog.InitializeAsync().GetAwaiter().GetResult();
        var vm = CreateViewModel(catalog);
        var image = new ImageFile(Path.Combine(_root, "missing.jpg"));
        vm.SelectedImage = image;
        var panel = new DevelopEditPanel
        {
            DataContext = vm
        };
        panel.Measure(new Size(250, 660));
        panel.Arrange(new Rect(0, 0, 250, 660));

        vm.CurrentCurve!.AddPointAndReturnIndex(0.5, 0.75);
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
        using var catalog = new CatalogService(_root);
        var vm = CreateViewModel(catalog);
        var raw = new ImageFile(Path.Combine(_root, "raw.dng"))
        {
            EditSettings = new EditSettings { Brightness = 37 }
        };
        var standard = new ImageFile(Path.Combine(_root, "standard.jpg"))
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
        using var catalog = new CatalogService(_root);
        var vm = CreateViewModel(catalog);
        vm.SelectedImage = new ImageFile(Path.Combine(_root, "first.jpg"));
        vm.IsShowingOriginal = true;

        vm.SelectedImage = new ImageFile(Path.Combine(_root, "second.jpg"));

        Assert.False(vm.IsShowingOriginal);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task LibraryMode_GatesBeforeAfterUndoAndRedo()
    {
        using var catalog = new CatalogService(_root);
        var vm = CreateViewModel(catalog);
        var image = new ImageFile(Path.Combine(_root, "library.jpg"))
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
    public async Task ScheduledHistogramRefresh_ClearsBeforeAfterState()
    {
        using var catalog = new CatalogService(_root);
        await catalog.InitializeAsync();
        var vm = new MainWindowViewModel(
            catalog,
            CreateSyntheticLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally))
        {
            IsDevelopMode = true
        };
        await vm.InitializeAsync();
        var image = new ImageFile(Path.Combine(_root, "missing.png"));
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(
            () => vm.IsWhiteBalanceReady && vm.PreviewImage != null);

        vm.Exposure = 1;
        await TestWaits.UntilAsync(() => image.EditSettings.Exposure == 1);
        await vm.ToggleBeforeAfterCommand.ExecuteAsync(null);
        Assert.True(vm.IsShowingOriginal);

        await TestWaits.UntilAsync(() => !vm.IsShowingOriginal);
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task DevelopToLibrary_ReschedulesHistogramFromThumbnailPixels()
    {
        using var catalog = new CatalogService(_root);
        await catalog.InitializeAsync();
        var vm = new MainWindowViewModel(
            catalog,
            CreateSyntheticLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        var image = new ImageFile(Path.Combine(_root, "histogram.png"));
        using var orange = new MagickImage(MagickColors.Orange, 16, 16);
        vm.Library.SetImages([image]);
        vm.Library.ReplaceThumbnail(
            image,
            BitmapConversionService.ConvertToBitmap(orange));
        vm.SelectedImage = image;
        var expectedLibrary = new HistogramService().CalculateLibraryHistogram(
            image.Thumbnail!);
        await TestWaits.UntilAsync(() => HistogramsMatch(
            vm.Histogram,
            expectedLibrary));

        vm.IsDevelopMode = true;
        await TestWaits.UntilAsync(() =>
            vm.PreviewImage != null &&
            vm.Histogram != null &&
            !HistogramsMatch(vm.Histogram, expectedLibrary));

        vm.IsDevelopMode = false;
        await TestWaits.UntilAsync(() => HistogramsMatch(
            vm.Histogram,
            expectedLibrary));

        vm.Library.ReplaceThumbnail(image, null);
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task DevelopToLibrary_WithoutThumbnailClearsRenderHistogram()
    {
        using var catalog = new CatalogService(_root);
        await catalog.InitializeAsync();
        var vm = new MainWindowViewModel(
            catalog,
            CreateSyntheticLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally))
        {
            IsDevelopMode = true
        };
        vm.SelectedImage = new ImageFile(Path.Combine(_root, "missing.png"));
        await TestWaits.UntilAsync(() => vm.Histogram != null);

        vm.IsDevelopMode = false;

        Assert.Null(vm.Histogram);
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task PresetHoverAndRestore_ClearBeforeAfterState()
    {
        using var catalog = new CatalogService(_root);
        await catalog.InitializeAsync();
        var vm = new MainWindowViewModel(
            catalog,
            CreateSyntheticLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally))
        {
            IsDevelopMode = true
        };
        await vm.InitializeAsync();
        vm.SelectedImage = new ImageFile(Path.Combine(_root, "missing.png"))
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

    [Fact]
    public async Task ReplacementDecode_UsesDelayedArmingIndicator()
    {
        using var catalog = new CatalogService(_root);
        var vm = CreateViewModel(catalog);
        var image = new ImageFile(Path.Combine(_root, "photo.dng"));
        vm.SelectedImage = image;
        var slow = new PreviewBaseRefreshState(
            image,
            requestId: 41,
            isRefreshing: true);

        vm.ApplyBaseRefreshState(slow);

        Assert.False(vm.IsBaseArming);
        await TestWaits.UntilAsync(() => vm.IsBaseArming);

        vm.ApplyBaseRefreshState(new PreviewBaseRefreshState(
            image,
            slow.RequestId,
            isRefreshing: false));
        Assert.False(vm.IsBaseArming);

        vm.ApplyBaseRefreshState(new PreviewBaseRefreshState(
            image,
            requestId: 42,
            isRefreshing: true));
        vm.ApplyBaseRefreshState(new PreviewBaseRefreshState(
            image,
            requestId: 42,
            isRefreshing: false));
        // Outlasts the 150ms arming delay to prove the superseded arm was
        // dropped; waiting longer only strengthens the absence.
        await Task.Delay(200);
        Assert.False(vm.IsBaseArming);

        await vm.DisposeAsync();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

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

    private static MainWindowViewModel CreateViewModel(CatalogService catalog) =>
        new(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask);
}
