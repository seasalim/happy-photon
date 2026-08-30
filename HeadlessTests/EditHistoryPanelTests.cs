using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class EditHistoryPanelTests
{
    [AvaloniaFact]
    public async Task DevelopShowsBoundedHistoryAbovePresetsWithCurrentStepAndClear()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        await catalog.InitializeAsync();
        var path = Path.Combine(root.Path, "photo.jpg");
        var id = await catalog.GetOrCreateImageAsync(path);
        var edited = new EditSettings { Exposure = .39 };
        var entries = Enumerable.Range(0, 40)
            .Select(index => new CatalogEditHistoryEntry(
                index,
                index == 0 ? "Original" : $"Exposure +{index / 100d:0.00}",
                new EditSettings { Exposure = index / 100d }))
            .ToArray();
        await catalog.SaveEditSettingsWithHistoryAsync(id, edited,
            new CatalogEditHistoryMutation(-1, entries, 39));
        var image = new ImageFile(path) { CatalogId = id, EditSettings = edited };
        await using var vm = new MainWindowViewModel(catalog, baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.Browse.SetImages([image]);
        vm.IsDevelopMode = true;
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded);

        var window = new MainWindow { Width = 800, Height = 500, DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs(); Dispatcher.UIThread.RunJobs();
        try
        {
            var history = window.FindControl<EditHistoryPanel>("EditHistoryPanel")!;
            var presets = window.FindControl<PresetsPanel>("PresetsPanel")!;
            var shared = window.FindControl<Grid>("DevelopLeftPane")!;
            var clear = history.FindControl<Button>("ClearHistoryButton")!;
            var rows = history.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("history-row")).ToArray();
            // The same 8px breathing row the Navigator gets separates History from Presets.
            Assert.True(presets.Bounds.Top - history.Bounds.Bottom >= 8);
            Assert.Equal(ScrollBarVisibility.Hidden,
                history.GetVisualDescendants().OfType<ScrollViewer>().First().VerticalScrollBarVisibility);
            Assert.True(history.Bounds.Height <= shared.Bounds.Height * .4 + 1);
            Assert.True(presets.Bounds.Height >= shared.Bounds.Height * .5);
            Assert.Equal(40, rows.Length);
            Assert.Single(rows, row => row.Classes.Contains("current"));
            // Rows stretch to the panel width so the current row's surface is full-width.
            Assert.All(rows, row => Assert.True(row.Bounds.Width >= history.Bounds.Width - 2));
            Assert.True(clear.IsVisible);
            var header = history.FindControl<Grid>("HistoryHeader")!;
            var label = history.FindControl<TextBlock>("HistoryLabel")!;
            // The header grid must span the toggle; the label and Clear share
            // a row without overlapping and Clear sits at the right edge.
            Assert.True(header.Bounds.Width >= history.Bounds.Width * .8);
            Assert.True(clear.Bounds.Left >= label.Bounds.Right);
            Assert.True(clear.Bounds.Right >= header.Bounds.Width - 1);

            await vm.JumpToHistoryStepCommand.ExecuteAsync(vm.HistoryEntries[^1]);
            Assert.Equal(0, image.EditSettings.Exposure);
            Assert.True(vm.ClearHistoryCommand.CanExecute(null));
            await vm.ClearHistoryCommand.ExecuteAsync(null);
            Assert.Empty(vm.HistoryEntries);
            Assert.False(vm.ClearHistoryCommand.CanExecute(null));
            Assert.False(clear.IsEffectivelyEnabled);
        }
        finally
        {
            window.DataContext = null;
            window.Close();
        }
    }
}
