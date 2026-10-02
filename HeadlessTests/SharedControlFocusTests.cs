using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SharedControlFocusTests
{
    [AvaloniaTheory]
    [MemberData(nameof(ThemeResourceTests.Variants), MemberType = typeof(ThemeResourceTests))]
    public void KeyboardOpenedMenu_ShowsFirstItemOutlineBeforeArrowNavigation(ThemeVariant theme)
    {
        var first = new MenuItem { Header = "First action" };
        var second = new MenuItem { Header = "Second action" };
        var menu = new MenuFlyout { Items = { first, second } };
        var button = new Button { Content = "Open menu", Flyout = menu };
        var window = new Window { Content = button, Width = 240, Height = 120 };
        using var scope = new TestUiScope(window, theme);

        try
        {
            Press(window, Key.Tab);
            AssertOutline(button, theme);
            Press(window, Key.Enter);
            Assert.True(menu.IsOpen);
            AssertOutline(first, theme);
            var popup = TopLevel.GetTopLevel(first)!;
            Press(popup, Key.Down);
            AssertOutline(second, theme);
            AssertNoOutline(first);
            Press(popup, Key.Escape);
            Assert.False(menu.IsOpen);
            var point = button.TranslatePoint(new Point(10, 10), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(menu.IsOpen);
            AssertNoOutline(first);
            AssertNoOutline(second);
        }
        finally
        {
            menu.Hide();
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(ThemeResourceTests.Variants), MemberType = typeof(ThemeResourceTests))]
    public void KeyboardFocus_UsesSharedOutlineForButtonSliderAndMenu(ThemeVariant theme)
    {
        var button = new Button { Content = "Quiet action", Classes = { "quiet-button" } };
        var slider = new Slider { Width = 200 };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        panel.Children.Add(button);
        panel.Children.Add(slider);
        var window = new Window { Content = panel, Width = 400, Height = 180 };
        using var scope = new TestUiScope(window, theme);

        Press(window, Key.Tab);
        AssertOutline(button, theme);
        Press(window, Key.Tab);
        AssertOutline(slider, theme);
        AssertNoOutline(button);
        var item = new MenuItem { Header = "Menu action" };
        var menu = new ContextMenu { Items = { new MenuItem { Header = "First action" }, item } };
        button.ContextMenu = menu;

        try
        {
            menu.Open(button);
            Dispatcher.UIThread.RunJobs();
            var popup = TopLevel.GetTopLevel(item)!;
            Press(popup, Key.Down);
            Press(popup, Key.Down);
            AssertOutline(item, theme);
            Press(popup, Key.Up);
            var first = (MenuItem)menu.Items[0]!;
            AssertOutline(first, theme);
            AssertNoOutline(item);
            popup.MouseMove(first.TranslatePoint(new Point(8, 8), popup)!.Value);
            Dispatcher.UIThread.RunJobs();
            AssertNoOutline(first);
        }
        finally
        {
            menu.Close();
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(ThemeResourceTests.Variants), MemberType = typeof(ThemeResourceTests))]
    public async Task BrowseKeyboardFocus_FollowsActivePhotoAndClearsOnDeparture(ThemeVariant theme)
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await using var vm = new MainWindowViewModel(catalog, new NullBaseLoader(), _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var images = new[] { new ImageFile("focus-first.jpg"), new ImageFile("focus-second.jpg") };
        vm.Browse.SetImages(images);
        vm.SelectedImage = images[0];
        var window = new MainWindow { Width = 1200, Height = 700 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        using var themeScope = new TestUiScope(theme: theme);
        var browse = window.FindControl<BrowseGridView>("BrowseGridView")!;

        for (var index = 0; index < 100 && !browse.IsFocused; index++)
        {
            Press(window, Key.Tab);
        }

        Assert.True(browse.IsFocused);
        Assert.Contains(":focus-visible", browse.Classes);
        AssertTile(images[0], true);
        AssertTile(images[1], false);
        AssertNoOutline(browse);
        Press(window, Key.Right);
        Assert.Same(images[1], vm.SelectedImage);
        AssertTile(images[0], false);
        AssertTile(images[1], true);
        Press(window, Key.Tab);
        Assert.False(browse.IsFocused);
        AssertTile(images[1], false);

        void AssertTile(ImageFile image, bool visible)
        {
            var tile = browse.GetVisualDescendants().OfType<Border>()
                .Single(border => border.Name == "ThumbnailTile" && ReferenceEquals(border.DataContext, image));
            var outline = tile.GetVisualDescendants().OfType<Border>()
                .Single(border => border.Classes.Contains("tile-focus"));
            Assert.False(tile.Focusable);
            Assert.Equal(visible, outline.IsVisible);
            Assert.Equal(new Thickness(2), tile.BorderThickness);
            Assert.Equal(new Thickness(1), outline.BorderThickness);
            Assert.Equal(default, outline.CornerRadius);
            Assert.Equal(ThemeResourceTests.Brush("TextPrimary", theme).Color,
                Assert.IsAssignableFrom<ISolidColorBrush>(outline.BorderBrush).Color);

            if (visible)
            {
                Assert.Equal(new Point(3, 3), outline.TranslatePoint(default, tile));
            }
        }
    }

    internal static void Press(TopLevel root, Key key)
    {
        root.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        root.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        root.UpdateLayout();
    }

    internal static void AssertOutline(Control control, ThemeVariant theme)
    {
        Assert.True(control.IsFocused);
        if (control is not MenuItem) Assert.Contains(":focus-visible", control.Classes);
        var outline = Assert.IsType<Border>(Assert.Single(Adorners(control)));
        Assert.Equal("SharedFocusOutline", outline.Name);
        Assert.Equal(new Thickness(1), outline.BorderThickness);
        Assert.Equal(default, outline.CornerRadius);
        Assert.Equal(new Thickness(-2), outline.Margin);
        Assert.False(AdornerLayer.GetIsClipEnabled(outline));
        Assert.Equal(ThemeResourceTests.Brush("TextPrimary", theme).Color,
            Assert.IsAssignableFrom<ISolidColorBrush>(outline.BorderBrush).Color);
    }

    private static void AssertNoOutline(Control control) => Assert.Empty(Adorners(control));

    private static IEnumerable<Control> Adorners(Control control) =>
        AdornerLayer.GetAdornerLayer(control)?.Children
            .Where(child => AdornerLayer.GetAdornedElement(child) == control) ?? [];
}
