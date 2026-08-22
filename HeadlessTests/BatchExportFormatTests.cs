using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
        var dialog = new BatchExportDialog(
            viewModel,
            [],
            ExportDialogMode.TourPreview);
        var configuration = dialog.FindControl<StackPanel>(
            "ConfigurationPanel")!;
        var slider = dialog.FindControl<Slider>("QualitySlider")!;

        foreach (var format in new[] { ExportFormat.Png, ExportFormat.Tiff })
        {
            dialog.ViewModel.SelectedFormatOption =
                dialog.ViewModel.FormatOptions.Single(
                    option => option.Format == format);
            Dispatcher.UIThread.RunJobs();

            Assert.True(configuration.IsVisible);
            Assert.True(slider.IsVisible);
            Assert.False(slider.IsEnabled);
        }

        dialog.Close();
        await viewModel.DisposeAsync();
    }

    public void Dispose() => _fixture.Dispose();
}
