using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DevelopReviewStateTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExpandedLocalDisclosuresStayTransparent(bool gray)
    {
        var theme = gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark;
        var view = new LocalsEditSection();
        var window = new Window { Content = view, Width = 250, Height = 1600 };
        using var scope = new TestUiScope(window, theme);

        foreach (var name in new[] { "LocalGeometryDisclosure", "LocalLuminanceDisclosure", "LocalHueDisclosure" })
        {
            var button = view.FindControl<ToggleButton>(name)!;
            button.IsChecked = true;
            var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().Single();
            output.WriteLine($"{theme}/{name}: checked background={presenter.Background}");
            Assert.Equal(0, ColorOf(presenter.Background).A);
        }
    }

    public static IEnumerable<object[]> ToggleOwners()
    {
        var ns = XNamespace.Get("https://github.com/avaloniaui");
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        foreach (var file in Directory.EnumerateFiles(
                     System.IO.Path.Combine(GoldenTestPaths.RepositoryRoot, "Views"), "*.axaml"))
        {
            var doc = XDocument.Load(file);

            if (!doc.Descendants(ns + "ToggleButton").Any(toggle =>
                    ((string?)toggle.Attribute("Classes") ?? "").Split(' ')
                    .Any(name => name is "icon-button" or "compact-button"))) continue;

            foreach (var gray in new[] { false, true })
            {
                yield return [(string)doc.Root!.Attribute(x + "Class")!, gray];
            }
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(ToggleOwners))]
    public void EverySharedToggleKeepsItsCheckedPresenterWhileHovered(string owner, bool gray)
    {
        var theme = gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark;
        var view = (Control)Activator.CreateInstance(typeof(CurveView).Assembly.GetType(owner)!)!;
        var window = new Window { Content = view, Width = 1200, Height = 1600 };
        using var scope = new TestUiScope(window, theme);
        var controls = view.GetLogicalDescendants().OfType<Control>().Prepend(view).ToArray();

        foreach (var control in controls)
        {
            control.IsVisible = true;
            control.IsEnabled = true;
        }

        window.UpdateLayout();
        var toggles = controls.OfType<ToggleButton>().Where(button =>
            button.Classes.Contains("icon-button") || button.Classes.Contains("compact-button")).ToArray();
        Assert.NotEmpty(toggles);

        foreach (var button in toggles)
        {
            button.ApplyTemplate();
            var presenter = button.GetVisualDescendants().OfType<ContentPresenter>()
                .Single(item => item.Name == "PART_ContentPresenter");

            foreach (var hovered in new[] { false, true })
            {
                ((IPseudoClasses)button.Classes).Set(":pointerover", hovered);

                foreach (var enabled in new[] { true, false })
                {
                    button.IsEnabled = enabled;

                    foreach (var pressed in new[] { false, true })
                    {
                        ((IPseudoClasses)button.Classes).Set(":pressed", pressed);
                        button.IsChecked = true;
                        Dispatcher.UIThread.RunJobs();
                        var background = ColorOf(presenter.Background);
                        output.WriteLine($"{owner}/{button.Name ?? button.Content}: checked, hover={hovered}, pressed={pressed}, enabled={enabled}: {background}");

                        if (button.Classes.Contains("compact-chevron"))
                        {
                            Assert.Equal(0, background.A);
                            var label = button.GetVisualDescendants().OfType<TextBlock>().Single();
                            var contrast = ThemeResourceTests.Contrast(ColorOf(label.Foreground),
                                ThemeResourceTests.Brush("SurfaceMid", theme).Color);
                            output.WriteLine($"Expanded label contrast: {contrast:F2}:1");
                            Assert.True(contrast >= 4.5);
                        }
                        else if (enabled && !pressed)
                        {
                            Assert.Equal(ThemeResourceTests.Brush("ControlActive", theme).Color, background);
                            Assert.Equal(ThemeResourceTests.Brush("OnControlActive", theme).Color,
                                ColorOf(presenter.Foreground));
                            button.IsChecked = false;
                            Dispatcher.UIThread.RunJobs();
                            Assert.NotEqual(background, ColorOf(presenter.Background));
                        }
                    }
                }

                button.IsEnabled = true;
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PointerClickKeepsToggleStateVisible(bool gray)
    {
        var theme = gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark;
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 16, Margin = new Thickness(20) };

        foreach (var classes in new[] { "compact-button", "icon-button", "icon-button compact" })
        {
            var button = new ToggleButton { Content = "1:1" };
            button.Classes.AddRange(classes.Split(' '));

            if (button.Classes.Contains("icon-button"))
            {
                button.Content = new Viewbox
                {
                    Child = new Avalonia.Controls.Shapes.Path
                    {
                        Data = Geometry.Parse("M2,6 L2,2 L6,2 M10,2 L14,2 L14,6 M14,10 L14,14 L10,14 M6,14 L2,14 L2,10"),
                        StrokeThickness = 1.5
                    }
                };
            }

            row.Children.Add(button);
        }

        var window = new Window
        {
            Content = row, Width = 260, Height = 80,
            Background = ThemeResourceTests.Brush("SurfaceLow", theme)
        };
        using var scope = new TestUiScope(window, theme);

        foreach (var button in row.Children.OfType<ToggleButton>())
        {
            var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, 12), window)!.Value;
            window.MouseMove(center);
            var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().Single();
            var uncheckedColor = ColorOf(presenter.Background);
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(button.IsPointerOver);
            Assert.True(button.IsChecked);
            Assert.NotEqual(uncheckedColor, ColorOf(presenter.Background));
            Assert.Equal(ThemeResourceTests.Brush("ControlActive", theme).Color, ColorOf(presenter.Background));
        }

        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var shots = System.IO.Directory.CreateDirectory(
            System.IO.Path.Combine(GoldenTestPaths.RepositoryRoot, "artifacts", "shots"));
        frame.Save(System.IO.Path.Combine(shots.FullName,
            $"wp7-review-toggle-hover-{(gray ? "gray" : "dark")}.png"), PngBitmapEncoderOptions.Default);
    }

    [AvaloniaTheory]
    [InlineData(200)]
    [InlineData(250)]
    public void FooterRequiredWidthIsUnchangedFromBase(int width)
    {
        var panel = new DevelopEditPanel();
        var window = new Window { Content = panel, Width = width, Height = 660 };
        using var scope = new TestUiScope(window, ThemeVariant.Dark);
        var bar = panel.FindControl<DevelopActionBar>("DevelopActionBar")!;
        var grid = (Grid)bar.Content!;
        var icons = (StackPanel)grid.Children[0];
        var reset = bar.FindControl<Button>("ResetAdjustmentsButton")!;
        var text = reset.GetVisualDescendants().OfType<TextBlock>().Single();
        var requiredReset = Math.Ceiling(text.TextLayout.WidthIncludingTrailingWhitespace) + reset.Padding.Left + reset.Padding.Right;
        output.WriteLine($"Pane {width}: footer available={bar.Bounds.Width}, icon group={icons.Bounds.Width}, Reset required={requiredReset}, Reset arranged={reset.Bounds.Width}, total required={icons.Bounds.Width + requiredReset}, text available={text.Bounds.Width}");
        Assert.Equal(155, icons.Bounds.Width);
        Assert.Equal(width - 30, bar.Bounds.Width);
        Assert.InRange(requiredReset, 16, 65);
    }

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
}
