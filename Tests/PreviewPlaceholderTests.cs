using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
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
        var beforeAfter = developPanel.FindControl<ToggleButton>(
            "BeforeAfterButton")!;
        var undo = developPanel.FindControl<Button>(
            "UndoEditButton")!;
        var redo = developPanel.FindControl<Button>(
            "RedoEditButton")!;
        var reset = developPanel.FindControl<Button>(
            "ResetAdjustmentsButton")!;
        var exportDialog = new BatchExportDialog(vm, [storedRaw]);
        var exportConfiguration = exportDialog.FindControl<StackPanel>(
            "ConfigurationPanel")!;
        var outputSharpening = exportDialog.FindControl<CheckBox>(
            "OutputSharpeningCheckBox")!;
        var closeExportDialog = exportDialog.FindControl<Button>(
            "CloseDialogButton")!;
        var exportButton = exportDialog.FindControl<Button>(
            "ExportButton")!;
        Assert.Null(developPanel.FindControl<CompactSlider>(
            "CaptureSharpenSlider"));
        Assert.Null(developPanel.FindControl<Grid>(
            "NoiseReductionControl"));
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
        Assert.True(outputSharpening.IsChecked);
        outputSharpening.IsChecked = false;
        Assert.False(vm.ExportSettings.OutputSharpening);
        outputSharpening.IsChecked = true;
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
        Assert.Equal("Return to Library", tourPrimaryAction.Content);
        tourExportDialog.Close();

        vm.SelectedImage = null;
        var image = new ImageFile(
            Path.Combine(GoldenTestPaths.AssetDirectory, "srgb-reference.jpg"));
        var otherImage = new ImageFile(
            Path.Combine(GoldenTestPaths.AssetDirectory, "display-p3-reference.jpg"));
        vm.Library.SetImages([image, otherImage]);
        vm.Library.ReplaceThumbnail(image, placeholder);
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

        vm.Library.ReplaceThumbnail(image, replacementPlaceholder);
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
        vm.Library.ReplaceThumbnail(image, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Throws<ObjectDisposedException>(
            () => _ = replacementPlaceholder.PixelSize);
        window.Close();
        await vm.DisposeAsync();
    }
}
