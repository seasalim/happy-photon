using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

// Approved WP6 gates, using the same loader-free scenes as the base probes.
// Each invocation creates three fresh scenes per theme and reports every sample.
public sealed class SharedControlBaselineTests(ITestOutputHelper output)
{
    private const int Runs = 3;

    [AvaloniaTheory]
    [MemberData(nameof(ThemeResourceTests.Variants), MemberType = typeof(ThemeResourceTests))]
    public void G2_LabelledCheckbox(ThemeVariant theme)
    {
        for (var run = 1; run <= Runs; run++)
        {
            var checkBox = new CheckBox
            {
                Content = "Labelled checkbox", IsChecked = true,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(checkBox);
            var window = new Window { Content = panel, Width = 400, Height = 150 };
            using var scope = new TestUiScope(window, theme);
            Settle(window);
            var box = checkBox.GetVisualDescendants().OfType<Border>()
                .Single(border => border.Name == "NormalRectangle");
            var label = checkBox.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Text == "Labelled checkbox");
            Assert.InRange(box.Bounds.Width, 11, 13);
            Assert.Equal(box.Bounds.Width, box.Bounds.Height);
            Assert.Equal(default, box.CornerRadius);
            Assert.True(checkBox.Bounds.Height >= 20);
            Assert.Equal(11, label.FontSize);
            var scale = box.RenderTransform?.Value.M11 ?? 1;
            Assert.Equal(1, scale);
            // Bounds are layout coordinates; TranslatePoint includes the render transform.
            var boxLayoutOrigin = box.GetVisualParent()!
                .TranslatePoint(box.Bounds.Position, checkBox)!.Value;
            var labelOrigin = label.TranslatePoint(default, checkBox)!.Value;
            var gap = labelOrigin.X - boxLayoutOrigin.X - box.Bounds.Width;
            Assert.InRange(gap, 5, 7);
            var drawnRight = box.TranslatePoint(new Point(box.Bounds.Width, 0), checkBox)!.Value.X;
            output.WriteLine(string.Concat(
                $"G2 theme={theme} run={run}: box={box.Bounds.Width}x{box.Bounds.Height}px; " +
                $"drawn={box.Bounds.Width * scale}px; scale={scale}; row={checkBox.Bounds.Height}px; " +
                $"labelGap={gap}px; drawnLabelGap={labelOrigin.X - drawnRight}px; labelFontSize={label.FontSize}"));
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(ThemeResourceTests.Variants), MemberType = typeof(ThemeResourceTests))]
    public void G3_BrowseTileContextMenu(ThemeVariant theme)
    {
        for (var run = 1; run <= Runs; run++)
        {
            var browse = NewBrowse();
            var window = new Window { Content = browse, Width = 800, Height = 600 };
            using var scope = new TestUiScope(window, theme);
            Settle(window);
            var tile = Tile(browse);
            var menu = tile.ContextMenu!;
            // The shipped tile menu carries no gestures; a fixed test-only gesture on its first item
            // gives the gesture typography check real text in both the base and final probes.
            menu.Items.OfType<MenuItem>().First().InputGesture = KeyGesture.Parse("Ctrl+Shift+F12");

            try
            {
                menu.Open(tile);
                Settle(window);

                foreach (var item in menu.Items.OfType<MenuItem>())
                {
                    var gesture = item.GetVisualDescendants().OfType<TextBlock>()
                        .Single(text => text.Name?.Contains("Gesture") == true);
                    Assert.InRange(item.Bounds.Height, 23, 25);
                    Assert.Equal(11, item.FontSize);
                    Assert.Equal(ThemeResourceTests.Resource<FontFamily>("FontLabel", theme), gesture.FontFamily);

                    if (item.InputGesture is not null)
                    {
                        Assert.True(gesture.IsEffectivelyVisible);
                        Assert.False(string.IsNullOrWhiteSpace(gesture.Text));
                    }

                    output.WriteLine(string.Concat(
                        $"G3 theme={theme} run={run}: item={item.Header}; height={item.Bounds.Height}px; " +
                        $"fontSize={item.FontSize}; gestureFamily={gesture.FontFamily}; " +
                        $"gestureIsBody={gesture.FontFamily.Equals(ThemeResourceTests.Resource<FontFamily>("FontBody", theme))}; " +
                        $"gestureVisible={gesture.IsVisible}; gestureText={gesture.Text}"));
                }
            }
            finally
            {
                menu.Close();
            }
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(ThemeResourceTests.Variants), MemberType = typeof(ThemeResourceTests))]
    public void G4_ButtonHeights(ThemeVariant theme)
    {
        for (var run = 1; run <= Runs; run++)
        {
            var panel = new StackPanel { Margin = new Thickness(20), Spacing = 8 };
            string[] classes = ["quiet-button", "compact-button", "icon-button", "icon-button compact"];
            var buttons = classes.Select(NewButton).ToArray();

            foreach (var button in buttons)
            {
                panel.Children.Add(button);
            }

            // Measure the legacy class on its shipped surface. This remains valid
            // after the local style include is deleted and its uses are renamed.
            var actionBar = new DevelopActionBar();
            panel.Children.Add(actionBar);
            var window = new Window { Content = panel, Width = 400, Height = 300 };
            using var scope = new TestUiScope(window, theme);
            Settle(window);

            for (var index = 0; index < buttons.Length; index++)
            {
                Assert.Equal(new double[] { 28, 24, 24, 20 }[index], buttons[index].Bounds.Height);
                var tokens = classes[index].Split(' ');
                var present = HasClassStyle(Application.Current!.Styles, tokens) || HasClassStyle(panel.Styles, tokens);
                output.WriteLine(string.Concat(
                    $"G4 theme={theme} run={run}: class={classes[index]}; " +
                    $"height={(present ? buttons[index].Bounds.Height.ToString(System.Globalization.CultureInfo.InvariantCulture) + "px" : "absent")}"));
            }

            var legacy = actionBar.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => button.Classes.Contains("icon-button"));
            output.WriteLine($"G4 theme={theme} run={run}: class=icon-button (action bar); " +
                $"height={(legacy is null ? "absent" : legacy.Bounds.Height + "px")}");
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(ThemeResourceTests.Variants), MemberType = typeof(ThemeResourceTests))]
    public void Acceptance1_HoverAndTooltip(ThemeVariant theme)
    {
        var browse = NewBrowse();
        var quiet = NewButton("quiet-button");
        var icon = NewButton("icon-button");
        var panel = ProbeGrid(quiet, icon, browse);
        var window = new Window { Content = panel, Width = 800, Height = 700 };
        using var scope = new TestUiScope(window, theme);
        Settle(window);
        var tile = Tile(browse);

        foreach (var control in new Control[] { tile, quiet, icon })
        {
            // Avoid animation time as a measurement variable in the hover skeleton.
            control.Transitions = null;
            window.MouseMove(control.TranslatePoint(new Point(control.Bounds.Width / 2, 12), window)!.Value);
            Settle(window);
            ReportBackground(theme, control);
        }

        var menu = tile.ContextMenu!;

        try
        {
            menu.Open(tile);
            Settle(window);
            var item = menu.Items.OfType<MenuItem>().First();
            var popup = TopLevel.GetTopLevel(item)!;
            popup.MouseMove(item.TranslatePoint(new Point(5, 5), popup)!.Value);
            Settle(popup);
            ReportBackground(theme, item);
        }
        finally
        {
            menu.Close();
        }

        ToolTip.SetTip(quiet, "Shared control tooltip probe");

        try
        {
            ToolTip.SetIsOpen(quiet, true);
            Settle(window);
            var tip = window.GetVisualDescendants().OfType<ToolTip>().Single();
            Assert.Equal(11, tip.FontSize);
            Assert.Equal(new Thickness(8, 4), tip.Padding);
            Assert.Equal(default, tip.CornerRadius);
            output.WriteLine($"Acceptance1 theme={theme}: tooltip fontSize={tip.FontSize}; " +
                $"padding={tip.Padding}; corners={tip.CornerRadius}");
        }
        finally
        {
            ToolTip.SetIsOpen(quiet, false);
        }
    }

    private void ReportBackground(ThemeVariant theme, Control control)
    {
        var presenter = control.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault();
        var background = control switch
        {
            Border border => border.Background,
            MenuItem item => item.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(border => border.Name == "PART_LayoutRoot")?.Background ?? item.Background,
            _ => presenter?.Background
        };
        Assert.True(control.IsPointerOver);
        var token = control.Classes.Contains("icon-button") ? "SurfaceHigh" : "ControlHover";
        Assert.Equal(ThemeResourceTests.Brush(token, theme).Color,
            Assert.IsAssignableFrom<ISolidColorBrush>(background).Color);
        output.WriteLine($"Acceptance1 theme={theme}: control={control.GetType().Name} " +
            $"classes={string.Join(" ", control.Classes)}; pointerOver={control.IsPointerOver}; background={background}; " +
            $"ControlHover={ThemeResourceTests.Brush("ControlHover", theme)}");
    }

    private static Grid ProbeGrid(Control first, Control second, Control third)
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        Grid.SetRow(second, 1);
        Grid.SetRow(third, 2);
        grid.Children.Add(first);
        grid.Children.Add(second);
        grid.Children.Add(third);

        return grid;
    }

    private static BrowseGridView NewBrowse()
    {
        var browse = new BrowseGridView
        {
            Images = new ObservableCollection<ImageFile>
            {
                // No catalog, loader, or original content reads are needed to realize a tile.
                new ImageFile(Path.Combine(Path.GetTempPath(), "wp6-baseline-unloaded.jpg"))
            }
        };
        // The main view model normally selects these modes. Select the grid in
        // this loader-free fixture so the inactive views cannot intercept hover.
        browse.FindControl<CompareView>("CompareView")!.IsVisible = false;
        browse.FindControl<LoupeView>("LoupeView")!.IsVisible = false;

        return browse;
    }

    private static Border Tile(BrowseGridView browse) => browse.GetVisualDescendants()
        .OfType<Border>().First(border => border.Name == "ThumbnailTile");

    private static Button NewButton(string classes)
    {
        var button = new Button { Content = "Probe", HorizontalAlignment = HorizontalAlignment.Left };

        foreach (var name in classes.Split(' '))
        {
            button.Classes.Add(name);
        }

        return button;
    }

    private static bool HasClassStyle(IStyle style, string[] classes)
    {
        if (style is Style leaf && classes.All(name => leaf.Selector?.ToString()?.Contains("." + name) == true))
            return true;
        if (style is StyleInclude include) return HasClassStyle(include.Loaded, classes);

        return style is Styles group && group.Any(child => HasClassStyle(child, classes));
    }

    private static void Settle(TopLevel root)
    {
        Dispatcher.UIThread.RunJobs();
        root.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}
