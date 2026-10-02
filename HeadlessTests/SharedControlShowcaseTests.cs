using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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

public sealed class SharedControlShowcaseTests
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
            ShowcaseTestHelper.Capture("context-menu-after-wp6", window, new PixelSize(600, 420),
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
            ShowcaseTestHelper.Capture("theme-menu-after-wp6", window, new PixelSize(900, 220),
                ThemeVariant.Dark, shown =>
                {
                    var button = titleBar.FindControl<Button>("AppearanceButton")!;
                    menu = Assert.IsType<MenuFlyout>(button.Flyout);
                    for (var index = 0; index < 20 && !button.IsFocused; index++)
                    {
                        SharedControlFocusTests.Press(shown, Key.Tab);
                    }

                    Assert.True(button.IsFocused);
                    SharedControlFocusTests.Press(shown, Key.Enter);
                    Assert.True(menu.IsOpen);
                    SharedControlFocusTests.AssertOutline(menu.Items.OfType<MenuItem>().First(), ThemeVariant.Dark);
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
    public async Task Develop_RendersShowcase()
    {
        await DevelopToolsBaselineTests.WithScene("normal", 2204, 1263, async (vm, scope) =>
        {
            ShowcaseTestHelper.Capture("develop-after-wp6", scope, new PixelSize(2204, 1263),
                ThemeVariant.Dark, window =>
                {
                    window.GetVisualDescendants().OfType<ScrollViewer>()
                        .Single(control => control.Name == "DevelopControlsScrollViewer").Offset = default;
                    Assert.True(window.GetVisualDescendants().OfType<Button>()
                        .Single(button => button.Name == "ResetAdjustmentsButton").IsEffectivelyVisible);
                });
            await Task.CompletedTask;
        });
    }

    [AvaloniaTheory]
    [MemberData(nameof(ThemeResourceTests.Variants), MemberType = typeof(ThemeResourceTests))]
    public void PasteSettings_RendersShowcase(ThemeVariant theme)
    {
        var targets = Enumerable.Range(0, 12).Select(_ => new EditSettings
        {
            Exposure = .5,
            Crop = new CropRegion { Left = .1, Top = .1, Right = .9, Bottom = .9 }
        }).ToArray();
        var model = new PasteSettingsViewModel("IMG_0412.CR2", 12, new Dictionary<string, bool>(), targets: targets);
        var dialog = new PasteSettingsDialog(model);
        var scene = theme == ThemeVariant.Dark ? "paste-settings-after-wp6" : "paste-settings-gray-after-wp6";
        ShowcaseTestHelper.Capture(scene, dialog, new PixelSize(660, 572), theme, shown =>
        {
            var crop = shown.GetVisualDescendants().OfType<CheckBox>()
                .Single(check => check.Content?.ToString() == "Crop & Straighten");
            Assert.True(crop.IsEffectivelyEnabled);
            Assert.False(crop.IsChecked);
            Assert.True(model.PhotoGroups.Single(group => group.Group.Name == "Crop & Straighten").HasOwnValues);
        });
    }

    [AvaloniaFact]
    public async Task DevelopDisabledReset_RendersShowcase()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await using var vm = new MainWindowViewModel(catalog, new NullBaseLoader(), _ => Task.CompletedTask);
        var bar = new DevelopActionBar { DataContext = vm, Margin = new Thickness(20) };
        var window = new Window { Content = bar,
            Background = ThemeResourceTests.Brush("SurfaceLow", ThemeVariant.Dark) };
        ShowcaseTestHelper.Capture("develop-reset-disabled-after-wp6", window, new PixelSize(420, 80),
            ThemeVariant.Dark, shown => Assert.False(bar.FindControl<Button>("ResetAdjustmentsButton")!.IsEffectivelyEnabled));
    }

    [AvaloniaTheory]
    [InlineData("tooltip-after-wp6")]
    [InlineData("focused-button-after-wp6")]
    [InlineData("focused-menu-item-after-wp6")]
    public void SharedFeedback_RendersShowcase(string scene)
    {
        var button = new Button { Content = "Quiet action", Classes = { "quiet-button" },
            HorizontalAlignment = HorizontalAlignment.Left };
        var panel = new StackPanel { Margin = new Thickness(30) };
        panel.Children.Add(button);
        var window = new Window { Content = panel,
            Background = ThemeResourceTests.Brush("SurfaceLow", ThemeVariant.Dark) };
        var item = new MenuItem { Header = "Copy path", InputGesture = KeyGesture.Parse("Ctrl+Shift+F12") };
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft,
            Items = { item, new MenuItem { Header = "Reveal" } } };
        button.Flyout = menu;
        ToolTip.SetTip(button, "Copy the selected photograph's path");

        try
        {
            ShowcaseTestHelper.Capture(scene, window, new PixelSize(420, 200), ThemeVariant.Dark, shown =>
            {
                if (scene == "focused-button-after-wp6")
                {
                    SharedControlFocusTests.Press(shown, Key.Tab);
                    SharedControlFocusTests.AssertOutline(button, ThemeVariant.Dark);
                }

                if (scene == "tooltip-after-wp6")
                {
                    ToolTip.SetIsOpen(button, true);
                    Dispatcher.UIThread.RunJobs();
                    var tip = shown.GetVisualDescendants().OfType<ToolTip>().Single();
                    ShowcaseTestHelper.Settle(() => tip.Opacity == 1, "Tooltip visible");
                }

                if (scene == "focused-menu-item-after-wp6")
                {
                    SharedControlFocusTests.Press(shown, Key.Tab);
                    SharedControlFocusTests.Press(shown, Key.Enter);
                    Assert.True(menu.IsOpen);
                    SharedControlFocusTests.AssertOutline(item, ThemeVariant.Dark);
                }
            });
        }
        finally
        {
            menu.Hide();
            ToolTip.SetIsOpen(button, false);
        }
    }
}
