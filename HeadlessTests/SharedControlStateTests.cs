using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SharedControlStateTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [MemberData(nameof(ThemeResourceTests.Variants), MemberType = typeof(ThemeResourceTests))]
    public void Checkbox_UncheckedBoundaryAndInteractionStatesAreVisible(ThemeVariant theme)
    {
        var check = new CheckBox { Content = "Include settings", IsChecked = false };
        var window = new Window { Content = check, Width = 240, Height = 80 };
        using var scope = new TestUiScope(window, theme);
        var box = check.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "NormalRectangle");
        var glyph = check.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>()
            .Single(path => path.Name == "CheckGlyph");
        var border = Assert.IsAssignableFrom<ISolidColorBrush>(box.BorderBrush).Color;

        foreach (var surface in new[] { "SurfaceLowest", "SurfaceLow", "SurfaceHigh", "SurfaceHighest" })
        {
            var contrast = ThemeResourceTests.Contrast(border, ThemeResourceTests.Brush(surface, theme).Color);
            output.WriteLine($"{theme}: unchecked boundary / {surface} = {contrast:F2}:1");
            Assert.True(contrast >= 3, $"{theme}: unchecked boundary / {surface} = {contrast:F2}:1");
        }

        Assert.Equal(0, glyph.Opacity);
        window.MouseMove(check.TranslatePoint(new Point(6, 10), window)!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ThemeResourceTests.Brush("TextSecondary", theme).Color,
            Assert.IsAssignableFrom<ISolidColorBrush>(box.BorderBrush).Color);
        check.IsEnabled = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ThemeResourceTests.Resource<double>("DisabledOpacity", theme), check.Opacity);
        var uncheckedFill = Assert.IsAssignableFrom<ISolidColorBrush>(box.Background).Color;
        check.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, glyph.Opacity);
        Assert.NotEqual(uncheckedFill, Assert.IsAssignableFrom<ISolidColorBrush>(box.Background).Color);
    }

    [AvaloniaTheory]
    [MemberData(nameof(ThemeResourceTests.Variants), MemberType = typeof(ThemeResourceTests))]
    public async Task DevelopReset_DisabledPresenterPreservesItsOriginalAppearance(ThemeVariant theme)
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(System.IO.Path.Combine(root.Path, "catalog"));
        await using var vm = new MainWindowViewModel(catalog, new NullBaseLoader(), _ => Task.CompletedTask);
        var bar = new DevelopActionBar { DataContext = vm };
        var window = new Window { Content = bar, Width = 500, Height = 80 };
        using var scope = new TestUiScope(window, theme);
        var reset = bar.FindControl<Button>("ResetAdjustmentsButton")!;
        var presenter = reset.GetVisualDescendants().OfType<ContentPresenter>()
            .Single(control => control.Name == "PART_ContentPresenter");
        Assert.False(reset.IsEffectivelyEnabled);
        output.WriteLine($"{theme}: Reset background={presenter.Background}; foreground={presenter.Foreground}");
        Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color.A);
        Assert.Equal(ThemeResourceTests.Brush("TextDisabled", theme).Color,
            Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Foreground).Color);
    }

    [AvaloniaTheory]
    [MemberData(nameof(ThemeResourceTests.Variants), MemberType = typeof(ThemeResourceTests))]
    public void CompactIcon_HasTwelvePixelPathAndOneCheckedState(ThemeVariant theme)
    {
        var path = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M0,0 L12,12"), StrokeThickness = 1 };
        var icon = new Viewbox { Child = path };
        var button = new ToggleButton { Classes = { "icon-button", "compact" }, Content = icon };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(button);
        var window = new Window { Content = panel, Width = 200, Height = 120 };
        using var scope = new TestUiScope(window, theme);
        var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().Single();
        Assert.Equal(new Size(20, 20), button.Bounds.Size);
        Assert.Equal(new Size(12, 12), icon.Bounds.Size);
        Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color.A);
        window.MouseMove(button.TranslatePoint(new Point(10, 10), window)!.Value);
        Dispatcher.UIThread.RunJobs();
        AssertBrush("SurfaceHigh", presenter.Background);
        button.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        AssertBrush("ControlActive", presenter.Background);
        AssertBrush("OnControlActive", presenter.Foreground);
        AssertBrush("OnControlActive", path.Stroke);
        window.MouseMove(new Point(180, 100));
        Dispatcher.UIThread.RunJobs();
        AssertBrush("ControlActive", presenter.Background);

        void AssertBrush(string token, IBrush? brush) => Assert.Equal(
            ThemeResourceTests.Brush(token, theme).Color, Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color);
    }
}
