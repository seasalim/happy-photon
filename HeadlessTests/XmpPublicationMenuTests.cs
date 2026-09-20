using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class XmpPublicationMenuTests
{
    [AvaloniaTheory]
    [InlineData(XmpSidecarMode.Off, false)]
    [InlineData(XmpSidecarMode.Read, false)]
    [InlineData(XmpSidecarMode.ReadWrite, true)]
    public async Task MenuState_TracksMode(XmpSidecarMode mode, bool enabled)
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(catalog, new NullBaseLoader(), _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.Browse.SetImages([new ImageFile("menu.jpg")]);
        vm.XmpSidecarMode = mode;
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm);
        Dispatcher.UIThread.RunJobs();
        var tile = window.GetVisualDescendants().OfType<Border>().First(c => c.Name == "ThumbnailTile");
        tile.ContextMenu!.Open(tile);
        try
        {
            Dispatcher.UIThread.RunJobs();
            var item = tile.ContextMenu.Items.OfType<MenuItem>().Single(i => i.Name == "WriteXmpSidecarsMenuItem");
            Assert.Equal(enabled, item.IsEnabled);
            Assert.True(ToolTip.GetShowOnDisabled(item));
            Assert.Contains("Read & write", ToolTip.GetTip(item)!.ToString());
            vm.XmpSidecarMode = XmpSidecarMode.ReadWrite;
            Dispatcher.UIThread.RunJobs();
            Assert.True(item.IsEnabled);
        }
        finally { tile.ContextMenu.Close(); }
    }

    [AvaloniaFact]
    public async Task BrowseContextMenu_RendersShowcase()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(catalog, new NullBaseLoader(), _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        using var thumbnail = new Bitmap(GoldenTestPaths.Asset("srgb-reference.jpg"));
        var image = new ImageFile("Coastal light.jpg") { Thumbnail = thumbnail, Rating = 4 };
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        vm.ToggleImageSelection(image);
        vm.XmpSidecarMode = XmpSidecarMode.ReadWrite;
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        ContextMenu? menu = null;
        try
        {
            ShowcaseTestHelper.Capture("browse-context-menu", scope, new PixelSize(1200, 700),
                ThemeVariant.Dark, staged =>
                {
                    var tile = staged.GetVisualDescendants().OfType<Border>().First(c => c.Name == "ThumbnailTile");
                    staged.MouseMove(tile.TranslatePoint(new Point(100, 70), staged)!.Value,
                        RawInputModifiers.None);
                    menu = tile.ContextMenu!;
                    menu.Open(tile);
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(menu.Items.OfType<MenuItem>().Single(i => i.Name == "WriteXmpSidecarsMenuItem").IsEnabled);
                });
        }
        finally
        {
            menu?.Close();
            image.Thumbnail = null;
        }
    }
}
