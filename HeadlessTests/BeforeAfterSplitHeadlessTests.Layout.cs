using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class BeforeAfterSplitHeadlessTests
{
    [AvaloniaFact]
    public async Task SplitToggleSitsBesideFullScreenInTheViewerBar()
    {
        using var catalog = await _fx.CreateCatalogAsync();
        await using var vm = _fx.CreateViewModel(
            catalog,
            new GrayLoader(),
            _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.IsDevelopMode = true;
        var image = new ImageFile(_fx.Path("narrow.jpg"));
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null);

        var window = new MainWindow { Width = 1280, Height = 800 };
        using var windowScope = TestUiScope.ForMainWindow(window, vm);
        Drain();

        var split = Descendant<ToggleButton>(window, "BeforeAfterSplitButton");
        var bar = Descendant<Border>(window, "DevelopControlBar");
        Assert.Contains(
            bar.GetVisualDescendants(),
            control => ReferenceEquals(control, split));
        Assert.True(split.IsEffectivelyVisible);

        var full = Descendant<Button>(window, "FullScreenButton");
        var row = Assert.IsType<StackPanel>(split.Parent);
        Assert.Same(row, full.Parent);
        Assert.Equal(row.Children.IndexOf(full) + 1, row.Children.IndexOf(split));

        window.Width = 800;
        window.Height = 600;
        window.UpdateLayout();
        Drain();

        Assert.False(split.IsEffectivelyVisible);
        var overflow = Descendant<Button>(window, "DevelopViewActionsButton");
        Assert.True(overflow.IsEffectivelyVisible);
        var menu = Assert.IsType<MenuFlyout>(overflow.Flyout);

        try
        {
            menu.ShowAt(overflow);
            Drain();
            Assert.True(menu.IsOpen);
            var item = Assert.Single(menu.Items.OfType<MenuItem>(),
                item => Equals(item.Header, "Before | After"));
            Assert.True(item.IsEffectivelyVisible);
            Assert.Same(vm.ToggleBeforeAfterSplitCommand, item.Command);
        }
        finally
        {
            menu.Hide();
        }
    }
}
