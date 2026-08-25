using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PreviewPlaceholderTests
{
    [AvaloniaFact]
    public async Task DevelopReachabilityControls_ReflectCommandAvailability()
    {
        using var catalog = new CatalogService(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-develop-controls-{Guid.NewGuid():N}"));
        await using var vm = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var images = new[]
        {
            new ImageFile(Path.Combine(catalog.CatalogPath, "first.jpg")),
            new ImageFile(Path.Combine(catalog.CatalogPath, "second.jpg"))
        };
        vm.Browse.SetImages(images);
        vm.SelectedImage = images[0];
        vm.IsDevelopMode = true;
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var panel = window.FindControl<DevelopEditPanel>("DevelopEditPanel")!;
        var actionBar = panel.FindControl<DevelopActionBar>("DevelopActionBar")!;
        var copy = actionBar.FindControl<Button>("CopyEditSettingsButton")!;
        var paste = actionBar.FindControl<Button>("PasteEditSettingsButton")!;
        var previous = window.FindControl<Button>("PreviousImageButton")!;
        var next = window.FindControl<Button>("NextImageButton")!;
        var fullScreen = window.FindControl<Button>("FullScreenButton")!;

        Assert.False(previous.IsEffectivelyEnabled);
        Assert.True(next.IsEffectivelyEnabled);
        Assert.True(fullScreen.IsEffectivelyEnabled);
        Assert.True(copy.IsEffectivelyEnabled);
        Assert.False(paste.IsEffectivelyEnabled);

        copy.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(paste.IsEffectivelyEnabled);
        next.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(previous.IsEffectivelyEnabled);
        Assert.False(next.IsEffectivelyEnabled);

        vm.IsCropMode = true;
        Dispatcher.UIThread.RunJobs();
        Assert.False(fullScreen.IsEffectivelyEnabled);
        vm.IsCropMode = false;
        vm.SelectedImage = null;
        Dispatcher.UIThread.RunJobs();
        Assert.False(fullScreen.IsEffectivelyEnabled);

        window.DataContext = null;
        window.Close();
    }

    [AvaloniaFact]
    public async Task FullScreenExitChip_RevealsExpiresAndCleansUpDeterministically()
    {
        using var catalog = new CatalogService(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-fullscreen-chip-{Guid.NewGuid():N}"));
        await using var vm = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var image = new ImageFile(Path.Combine(catalog.CatalogPath, "photo.jpg"));
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        var clock = new TestTimeProvider();
        var window = new MainWindow
        {
            DataContext = vm,
            FullScreenExitTimeProvider = clock
        };
        window.Show();
        vm.ToggleFullScreenCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var chip = window.FindControl<Button>("FullScreenExitButton")!;

        Assert.Equal(0, chip.Opacity);
        Assert.False(chip.IsHitTestVisible);
        window.MouseMove(new Point(20, 20), RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.True(chip.Opacity > 0);
        Assert.True(chip.IsHitTestVisible);
        Assert.True(window.IsFullScreenExitTimerActive);

        clock.Advance(TimeSpan.FromMilliseconds(1900));
        Dispatcher.UIThread.RunJobs();
        Assert.True(chip.IsHitTestVisible);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        Dispatcher.UIThread.RunJobs();
        Assert.False(chip.IsHitTestVisible);

        // Dragging the photograph must reveal the way out too: the viewer marks
        // pointer moves handled while panning, so the chip listens on the tunnel.
        window.MouseDown(new Point(200, 200), MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(new Point(260, 240), RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();
        Assert.True(chip.IsHitTestVisible);
        window.MouseUp(new Point(260, 240), MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        clock.Advance(TimeSpan.FromSeconds(2));
        Dispatcher.UIThread.RunJobs();
        Assert.False(chip.IsHitTestVisible);

        window.MouseMove(new Point(30, 30), RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        chip.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.IsFullScreenMode);
        Assert.False(window.IsFullScreenExitTimerActive);
        Assert.False(chip.IsHitTestVisible);

        vm.ToggleFullScreenCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.MouseMove(new Point(40, 40), RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.IsFullScreenExitTimerActive);
        window.DataContext = null;
        window.Close();
        Assert.False(window.IsFullScreenExitTimerActive);
    }

    [AvaloniaFact]
    public async Task Placeholder_HidesWhenPreviewArrives()
    {
        Dispatcher.UIThread.RunJobs();
        using var catalog = new CatalogService(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-placeholder-{Guid.NewGuid():N}"));
        var vm = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask);
        var storedRaw = new ImageFile(
            Path.Combine(catalog.CatalogPath, "missing.dng"));
        vm.SelectedImage = storedRaw;
        var window = new MainWindow
        {
            DataContext = vm
        };
        var placeholder = new Bitmap(
            Path.Combine(GoldenTestPaths.AssetDirectory, "srgb-reference.jpg"));
        var replacementPlaceholder = new Bitmap(
            Path.Combine(GoldenTestPaths.AssetDirectory, "srgb-reference.jpg"));
        using var preview = new Bitmap(
            Path.Combine(GoldenTestPaths.AssetDirectory, "display-p3-reference.jpg"));
        using var editedPreview = new Bitmap(
            Path.Combine(GoldenTestPaths.AssetDirectory, "adobe-rgb-reference.jpg"));
        var developPlaceholder = window.FindControl<Image>(
            "DevelopPlaceholderImage")!;
        var fullScreenPlaceholder = window.FindControl<Image>(
            "FullScreenPlaceholderImage")!;
        var fullScreenSelectionBadge = window.FindControl<Border>(
            "FullScreenSelectionBadge")!;
        var navigatorThumbnail = window.FindControl<Image>(
            "NavigatorThumbnailImage")!;
        var navigatorPreview = window.FindControl<Image>(
            "NavigatorPreviewImage")!;
        var developPanel = window.FindControl<DevelopEditPanel>(
            "DevelopEditPanel")!;
        var whiteBalanceMode = developPanel.FindControl<ComboBox>(
            "WhiteBalanceModeBox")!;
        var whiteBalanceAuto = developPanel.FindControl<Button>(
            "WhiteBalanceAutoButton")!;
        var whiteBalancePicker = developPanel.FindControl<ToggleButton>(
            "WhiteBalancePickerButton")!;
        var whiteBalanceHeading = developPanel.FindControl<TextBlock>(
            "WhiteBalanceHeading")!;
        var actionBar = developPanel.FindControl<DevelopActionBar>(
            "DevelopActionBar")!;
        var beforeAfter = actionBar.FindControl<ToggleButton>(
            "BeforeAfterButton")!;
        var undo = actionBar.FindControl<Button>(
            "UndoEditButton")!;
        var redo = actionBar.FindControl<Button>(
            "RedoEditButton")!;
        var reset = actionBar.FindControl<Button>(
            "ResetAdjustmentsButton")!;
        var exportDialog = new BatchExportDialog(vm, [storedRaw]);
        var exportConfiguration = exportDialog.FindControl<StackPanel>(
            "ConfigurationPanel")!;
        var outputSharpeningOff = exportDialog.FindControl<RadioButton>(
            "OutputSharpeningOffButton")!;
        var outputSharpeningScreen = exportDialog.FindControl<RadioButton>(
            "OutputSharpeningScreenButton")!;
        var outputSharpeningPrint = exportDialog.FindControl<RadioButton>(
            "OutputSharpeningPrintButton")!;
        var outputColorSpace = exportDialog.FindControl<ComboBox>(
            "OutputColorSpaceBox")!;
        var closeExportDialog = exportDialog.FindControl<Button>(
            "CloseDialogButton")!;
        var exportButton = exportDialog.FindControl<Button>(
            "ExportButton")!;
        Assert.Null(developPanel.FindControl<CompactSlider>(
            "CaptureSharpenSlider"));
        Assert.Null(developPanel.FindControl<CompactSlider>(
            "LuminanceNrSlider"));
        Assert.Null(developPanel.FindControl<ComboBox>(
            "HighlightReconstructionBox"));
        Assert.Equal(
            "Toggle original preview",
            AutomationProperties.GetName(beforeAfter));
        Assert.Equal("Undo edit", AutomationProperties.GetName(undo));
        Assert.Equal("Redo edit", AutomationProperties.GetName(redo));
        Assert.Equal("Reset adjustments", AutomationProperties.GetName(reset));
        Assert.True(exportConfiguration.IsVisible);
        Assert.Equal(HorizontalAlignment.Center,
            closeExportDialog.HorizontalContentAlignment);
        Assert.Equal(112, exportButton.MinWidth);
        Assert.Equal(HorizontalAlignment.Center,
            exportButton.HorizontalContentAlignment);
        Assert.True(outputSharpeningScreen.IsChecked);
        Assert.Equal(0, outputColorSpace.SelectedIndex);
        outputColorSpace.SelectedIndex = 1;
        Assert.Equal(OutputColorSpace.DisplayP3, vm.ExportSettings.OutputColorSpace);
        outputSharpeningOff.IsChecked = true;
        Assert.Equal(OutputSharpeningMode.Off, vm.ExportSettings.OutputSharpening);
        outputSharpeningPrint.IsChecked = true;
        Assert.Equal(OutputSharpeningMode.Print, vm.ExportSettings.OutputSharpening);
        Assert.False(vm.CanUndo);
        Assert.False(fullScreenSelectionBadge.IsVisible);
        exportDialog.Close();

        var tourExportDialog = new BatchExportDialog(
            vm,
            [],
            ExportDialogMode.TourPreview);
        var tourConfiguration = tourExportDialog.FindControl<StackPanel>(
            "ConfigurationPanel")!;
        var tourPrimaryAction = tourExportDialog.FindControl<Button>(
            "ExportButton")!;
        Assert.True(tourConfiguration.IsVisible);
        Assert.Equal("Return to Browse", tourPrimaryAction.Content);
        tourExportDialog.Close();

        vm.SelectedImage = null;
        var image = new ImageFile(
            Path.Combine(GoldenTestPaths.AssetDirectory, "srgb-reference.jpg"));
        var otherImage = new ImageFile(
            Path.Combine(GoldenTestPaths.AssetDirectory, "display-p3-reference.jpg"));
        vm.Browse.SetImages([image, otherImage]);
        vm.Browse.ReplaceThumbnail(image, placeholder);
        vm.SelectedImage = image;

        Assert.Same(placeholder, navigatorThumbnail.Source);
        Assert.Equal(28, whiteBalanceMode.Height);
        Assert.Equal(11, whiteBalanceMode.FontSize);
        Assert.Equal(28, whiteBalanceAuto.Height);
        Assert.Equal(50, whiteBalanceAuto.MinWidth);
        Assert.Equal(11, whiteBalanceAuto.FontSize);
        Assert.Equal(28, whiteBalancePicker.Height);
        Assert.Equal(28, whiteBalancePicker.MinWidth);
        Assert.Equal(12, whiteBalanceHeading.FontSize);
        Assert.Equal(1, whiteBalanceHeading.LetterSpacing);
        Assert.Same(placeholder, developPlaceholder.Source);
        Assert.Same(placeholder, fullScreenPlaceholder.Source);
        Assert.True(developPlaceholder.IsVisible);
        Assert.True(fullScreenPlaceholder.IsVisible);

        vm.Browse.ReplaceThumbnail(image, replacementPlaceholder);
        Assert.Same(replacementPlaceholder, navigatorThumbnail.Source);
        Assert.Same(replacementPlaceholder, developPlaceholder.Source);
        Assert.Same(replacementPlaceholder, fullScreenPlaceholder.Source);
        Assert.Equal(placeholder.PixelSize, replacementPlaceholder.PixelSize);
        Dispatcher.UIThread.RunJobs();
        Assert.Throws<ObjectDisposedException>(() => _ = placeholder.PixelSize);

        vm.PreviewImage = preview;
        Assert.False(developPlaceholder.IsVisible);
        Assert.False(fullScreenPlaceholder.IsVisible);
        Assert.Same(preview, navigatorPreview.Source);

        vm.PreviewImage = editedPreview;
        Assert.Same(editedPreview, navigatorPreview.Source);

        vm.ToggleImageSelection(image);
        vm.ToggleImageSelection(otherImage);
        vm.ToggleFullScreenCommand.Execute(null);
        Assert.True(fullScreenSelectionBadge.IsVisible);
        Assert.Equal(
            "SELECTION · 1 / 2",
            Assert.IsType<TextBlock>(fullScreenSelectionBadge.Child).Text);
        vm.SelectNextImageCommand.Execute(null);
        Assert.Equal(
            "SELECTION · 2 / 2",
            Assert.IsType<TextBlock>(fullScreenSelectionBadge.Child).Text);
        vm.ToggleFullScreenCommand.Execute(null);
        Assert.False(fullScreenSelectionBadge.IsVisible);

        vm.PreviewImage = null;
        window.DataContext = null;
        vm.Browse.ReplaceThumbnail(image, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Throws<ObjectDisposedException>(
            () => _ = replacementPlaceholder.PixelSize);
        window.Close();
        await vm.DisposeAsync();
    }
}
