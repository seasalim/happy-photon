using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BatchExportFormatTests : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("export-format");

    [AvaloniaFact]
    public async Task LosslessQualitySlider_RemainsVisibleAndDisabled()
    {
        using var catalog = _fixture.CreateCatalog();
        var viewModel = _fixture.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask);
        viewModel.SwitchToExportCommand.Execute(null);
        var pane = new ExportSettingsPane { DataContext = viewModel };
        var slider = pane.FindControl<Slider>("ExportQualitySlider")!;
        var countLine = pane.FindControl<TextBlock>("ExportCountLineText")!;
        var exportButton = pane.FindControl<Button>("RunExportButton")!;

        Assert.Same(countLine.Parent, exportButton.Parent);

        foreach (var format in new[] { ExportFormat.Png, ExportFormat.Tiff })
        {
            viewModel.ExportSettings.Format = format;
            Dispatcher.UIThread.RunJobs();

            Assert.True(slider.IsVisible);
            Assert.False(slider.IsEnabled);
        }

        await viewModel.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task ExportPrimaryAction_IsFilledFullWidthBelowCountLine()
    {
        using var catalog = _fixture.CreateCatalog();
        var viewModel = _fixture.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask);
        viewModel.SwitchToExportCommand.Execute(null);
        var pane = new ExportSettingsPane { DataContext = viewModel };
        var window = new Window { Content = pane };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var countLine = pane.FindControl<TextBlock>("ExportCountLineText")!;
        var exportButton = pane.FindControl<Button>("RunExportButton")!;
        var actionStack = Assert.IsType<StackPanel>(countLine.Parent);

        Assert.Same(actionStack, exportButton.Parent);
        Assert.Equal(Orientation.Vertical, actionStack.Orientation);
        Assert.True(
            actionStack.Children.IndexOf(countLine) <
            actionStack.Children.IndexOf(exportButton));
        Assert.Equal(10, actionStack.Spacing);
        Assert.Equal("EXPORT", exportButton.Content);
        Assert.Equal(30, exportButton.Height);
        Assert.Equal(HorizontalAlignment.Stretch, exportButton.HorizontalAlignment);
        Assert.Equal(
            HorizontalAlignment.Center,
            exportButton.HorizontalContentAlignment);
        Assert.Equal(
            VerticalAlignment.Center,
            exportButton.VerticalContentAlignment);
        Assert.Equal(new CornerRadius(8), exportButton.CornerRadius);
        Assert.Equal(FontWeight.Bold, exportButton.FontWeight);
        Assert.Equal(11, exportButton.FontSize);
        Assert.Equal(2, exportButton.LetterSpacing);
        Assert.Equal(
            ThemeResourceTests.Resource<FontFamily>("FontLabel", ThemeVariant.Dark),
            exportButton.FontFamily);
        Assert.Equal(
            ThemeResourceTests.Brush("PrimaryContainer", ThemeVariant.Dark).Color,
            Assert.IsType<SolidColorBrush>(exportButton.Background).Color);
        Assert.Equal(
            ThemeResourceTests.Brush("OnPrimary", ThemeVariant.Dark).Color,
            Assert.IsType<SolidColorBrush>(exportButton.Foreground).Color);

        window.Close();
        await viewModel.DisposeAsync();
    }

    public void Dispose() => _fixture.Dispose();
}
