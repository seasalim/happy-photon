using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class TypeScaleShowcaseTests
{
    [AvaloniaFact]
    public async Task BrowseContextMenu_RendersShowcase()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await using var vm = new MainWindowViewModel(catalog, new NullBaseLoader(),
            _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var image = new ImageFile("IMG_0412.CR2") { IsSelected = true, IsActive = true };
        var browse = new BrowseGridView
        {
            DataContext = vm,
            Images = new ObservableCollection<ImageFile> { image },
            SelectedImage = image
        };
        var window = new Window { Content = browse };
        ContextMenu? menu = null;

        try
        {
            ShowcaseTestHelper.Capture("04-browse-context-menu", window, new PixelSize(600, 420),
                ThemeVariant.Dark, shown =>
                {
                    var tile = Assert.Single(browse.GetVisualDescendants().OfType<Border>(),
                        border => border.Classes.Contains("thumbnail"));
                    menu = tile.ContextMenu!;
                    menu.PlacementTarget = tile;
                    menu.Placement = PlacementMode.Right;
                    menu.Open();
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(menu.IsOpen);
                    Assert.All(menu.Items.OfType<MenuItem>(), item => Assert.Equal(11, item.FontSize));
                });
        }
        finally
        {
            menu?.Close();
        }
    }

    [AvaloniaFact]
    public async Task ThemeMenu_RendersShowcase()
    {
        using var theme = new TestUiScope(theme: ThemeVariant.Dark);
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await using var vm = new MainWindowViewModel(catalog, new NullBaseLoader(),
            _ => Task.CompletedTask);
        vm.RestoreAppTheme(AppTheme.Dark);
        var titleBar = new HappyPhotonTitleBar { DataContext = vm, VerticalAlignment = VerticalAlignment.Top };
        var window = new Window { Content = titleBar };
        MenuFlyout? menu = null;

        try
        {
            ShowcaseTestHelper.Capture("16-theme-menu", window, new PixelSize(900, 220),
                ThemeVariant.Dark, shown =>
                {
                    var button = titleBar.FindControl<Button>("AppearanceButton")!;
                    menu = Assert.IsType<MenuFlyout>(button.Flyout);
                    menu.ShowAt(button);
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(menu.IsOpen);
                    Assert.All(menu.Items.OfType<MenuItem>(), item => Assert.Equal(11, item.FontSize));
                });
        }
        finally
        {
            menu?.Hide();
            titleBar.DataContext = null;
        }
    }

    [AvaloniaFact]
    public async Task SettingsGeneral_RendersShowcase()
    {
        using var theme = new TestUiScope(theme: ThemeVariant.Dark);
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await using var vm = new MainWindowViewModel(catalog, new NullBaseLoader(),
            _ => Task.CompletedTask);
        vm.RestoreAppTheme(AppTheme.Dark);
        var dialog = new SettingsDialog(vm);

        ShowcaseTestHelper.Capture("17-settings-general", dialog, new PixelSize(650, 610),
            ThemeVariant.Dark, shown =>
                Assert.Equal(0, shown.FindControl<TabControl>("SettingsTabs")!.SelectedIndex));
    }

    [AvaloniaFact]
    public void MoveToTrash_RendersShowcase()
    {
        using var theme = new TestUiScope(theme: ThemeVariant.Dark);
        var dialog = new ConfirmationDialog("Move to Trash", "Move \"4.2.03.tiff\" to Trash?",
            ConfirmationDialogButtons.YesNo, destructive: true)
        {
            SizeToContent = SizeToContent.Manual
        };

        ShowcaseTestHelper.Capture("29-confirmation", dialog, new PixelSize(420, 110),
            ThemeVariant.Dark);
    }
}
