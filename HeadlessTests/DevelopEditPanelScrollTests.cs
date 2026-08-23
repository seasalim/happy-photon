using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DevelopEditPanelScrollTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();

    [AvaloniaFact]
    public async Task DevelopEntriesAtCompactHeightStartAtTop()
    {
        using var catalog = new CatalogService(_root.Path);
        await using var viewModel = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask);
        var panel = new DevelopEditPanel
        {
            DataContext = viewModel,
            IsVisible = false
        };
        var window = new Window
        {
            Width = 250,
            Height = 500,
            Content = panel
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            panel.IsVisible = true;
            viewModel.IsDevelopMode = true;
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();

            var scrollViewer = panel.FindControl<ScrollViewer>(
                "DevelopControlsScrollViewer")!;

            var firstEntryOffset = scrollViewer.Offset.Y;

            scrollViewer.Offset = new Vector(0, 1_200);
            panel.IsVisible = false;
            viewModel.IsDevelopMode = false;
            Dispatcher.UIThread.RunJobs();
            panel.IsVisible = true;
            viewModel.IsDevelopMode = true;
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(
                (FirstEntry: 0d, Reentry: 0d),
                (FirstEntry: firstEntryOffset,
                 Reentry: scrollViewer.Offset.Y));
        }
        finally
        {
            window.Close();
            panel.DataContext = null;
        }
    }

    public void Dispose() => _root.Dispose();
}
