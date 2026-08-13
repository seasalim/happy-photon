using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

// Phase 0 (2026-08-12) measured hidden indeterminate ProgressBars as the
// entire idle CPU/GPU cost (B1 ~0.3 % CPU / ~2.9 % GPU, B2 ~2.2 % / ~13.4 %,
// both exactly floor with the bars disabled). These tests pin the selected
// remediation: a bar is indeterminate only while its represented work runs,
// and library tiles use a static placeholder instead of a ProgressBar.
public sealed class IdleAnimationQuiescenceTests
{
    [AvaloniaFact]
    public async Task ChromeBars_AreIndeterminateOnlyWhileTheirWorkRuns()
    {
        using var catalog = new CatalogService(NewRoot());
        var vm = NewViewModel(catalog);
        var gate = new StartupGateView { DataContext = vm };
        var develop = new DevelopEditPanel { DataContext = vm };
        var window = new Window
        {
            Content = new StackPanel { Children = { gate, develop } }
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var startupBar = GetBar(gate, "StartupProgressBar");
        var firstRunBar = GetBar(gate, "FirstRunProgressBar");
        var armingBar = GetBar(develop, "BaseArmingProgressBar");

        Assert.True(vm.IsStartupInitializing);
        Assert.True(startupBar.IsIndeterminate);
        Assert.False(firstRunBar.IsIndeterminate);
        Assert.False(armingBar.IsIndeterminate);

        vm.ShowFirstRunWelcome(null);
        vm.IsFirstRunBusy = true;
        Dispatcher.UIThread.RunJobs();
        Assert.False(startupBar.IsIndeterminate);
        Assert.True(firstRunBar.IsIndeterminate);

        vm.IsFirstRunBusy = false;
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.IsBaseArming = true;
        Dispatcher.UIThread.RunJobs();
        Assert.False(startupBar.IsIndeterminate);
        Assert.False(firstRunBar.IsIndeterminate);
        Assert.True(armingBar.IsIndeterminate);

        vm.IsBaseArming = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(armingBar.IsIndeterminate);

        window.Close();
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task LibraryTiles_UseStaticLoadingPlaceholderWithoutProgressBars()
    {
        var root = NewRoot();
        using var catalog = new CatalogService(root);
        var vm = NewViewModel(catalog);
        var image = new ImageFile(Path.Combine(root, "a.jpg")) { IsLoading = true };
        vm.Library.SetImages([image]);
        var grid = new LibraryGridView
        {
            DataContext = vm,
            Images = vm.Library.VisibleImages
        };
        var window = new Window { Content = grid };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(grid.GetVisualDescendants().OfType<ProgressBar>());
        var placeholder = grid.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Name == "TileLoadingPlaceholder");
        Assert.True(placeholder.IsVisible);

        image.IsLoading = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(placeholder.IsVisible);

        window.Close();
        await vm.DisposeAsync();
    }

    // Logical tree: a hidden view's ScrollViewer never templates, so its
    // children may not be visual-tree members until first shown.
    private static ProgressBar GetBar(Control view, string name) =>
        view.GetLogicalDescendants().OfType<ProgressBar>()
            .Single(bar => bar.Name == name);

    private static string NewRoot() =>
        Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-idle-quiescence-{Guid.NewGuid():N}")).FullName;

    private static MainWindowViewModel NewViewModel(CatalogService catalog) =>
        new(catalog, baseLoader: null, loadMetadataAsync: _ => Task.CompletedTask);
}
