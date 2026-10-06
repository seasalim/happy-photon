using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
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

// Owner-approved VISUALS-WP7 G2/G4 gates in both themes.
public sealed class DevelopControlsMeasurementTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScopeCardUsesCompactIcons(bool gray)
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        await using var vm = new MainWindowViewModel(catalog)
        {
            IsDevelopMode = true
        };
        var panel = new DevelopEditPanel { DataContext = vm };
        var window = new Window { Width = 250, Height = 660, Content = panel };
        using var scope = new TestUiScope(window, Theme(gray));
        var histogram = panel.FindControl<HistogramView>("DevelopHistogram")!;
        histogram.Histogram = new HistogramData();
        Settle(window);
        var selector = panel.FindControl<ScopeSelectorRow>("ScopeSelector")!;
        var header = (Grid)selector.Content!;
        var modes = new[]
        {
            "HistogramScopeButton", "RawHistogramScopeButton", "WaveformScopeButton"
        }.Select(name => Size(selector.FindControl<ToggleButton>(name)!)).ToArray();
        var clipping = new[]
        {
            "DisplayFloorTriangleTarget", "SceneHighlightTriangleTarget"
        }.Select(name => Size(histogram.FindControl<Control>(name)!)).ToArray();

        output.WriteLine("G2 " + JsonSerializer.Serialize(new
        {
            theme = Theme(gray).Key,
            paneWidth = panel.Bounds.Width,
            headerHeight = header.Bounds.Height,
            selectorHeight = selector.Bounds.Height,
            headerMargin = header.Margin.ToString(),
            modes,
            clipping
        }));
        Assert.Equal(24, header.Bounds.Height);
        Assert.All(new[] { "HistogramScopeButton", "RawHistogramScopeButton", "WaveformScopeButton" },
            name => Assert.Equal(new Size(20, 20), selector.FindControl<ToggleButton>(name)!.Bounds.Size));
        var toggles = ((StackPanel)header.Children[1]).Children.Cast<ToggleButton>().ToArray();
        Assert.Equal(new[] { "HistogramScopeButton", "RawHistogramScopeButton", "WaveformScopeButton" },
            toggles.Select(toggle => toggle.Name));
        Assert.Equal(new[] { "Histogram scope", "RAW histogram scope", "Waveform scope" },
            toggles.Select(AutomationProperties.GetName));
        Assert.Equal(new object?[] { "Histogram", vm.RawHistogramHint, "Waveform" },
            toggles.Select(ToolTip.GetTip));
        Assert.All(new[] { "DisplayFloorTriangleTarget", "SceneHighlightTriangleTarget" },
            name => Assert.Equal(new Size(20, 20), histogram.FindControl<ToggleButton>(name)!.Bounds.Size));
        var canvas = histogram.FindControl<Canvas>("HistogramCanvas")!;
        Assert.All(new[] { "DisplayFloorTriangle", "SceneHighlightTriangle" }, name => Assert.Equal(0,
            histogram.FindControl<Avalonia.Controls.Shapes.Path>(name)!.TranslatePoint(default, canvas)!.Value.Y));
        panel.DataContext = null;
    }

    // FIXES-DEVELOP-WP10: the scope band takes the Navigator's anatomy; left-pane values pinned at d857c05.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScopeBandsMatchTheNavigatorBand(bool gray)
    {
        await DevelopToolsBaselineTests.WithScene("normal", 1200, 700, async (vm, scope) =>
        {
            vm.AppTheme = gray ? AppTheme.MidGray : AppTheme.Dark;
            // test-teardown-policy: allow - WithScene owns the MainWindow scope.
            scope.Show();
            var window = scope.Window!;
            ShellPaneLimitsTests.Settle(window);
            var left = window.FindControl<Border>("WorkspaceLeftPanel")!;
            var navigator = window.FindControl<Border>("NavigatorPanel")!;
            var navigatorHeader = window.FindControl<Grid>("NavigatorHeader")!;
            var navigatorTitle = navigatorHeader.Children[0];
            var navigatorWell = window.FindControl<Border>("NavigatorPreviewFrame")!;
            var presets = window.GetVisualDescendants().OfType<PresetsPanel>().Single();
            var panel = window.FindControl<DevelopEditPanel>("DevelopEditPanel")!;
            var band = panel.FindControl<Border>("DevelopScopeBox")!;
            var header = (Control)panel.FindControl<ScopeSelectorRow>("ScopeSelector")!.Content!;
            var title = panel.FindControl<ScopeSelectorRow>("ScopeSelector")!.FindControl<TextBlock>("ScopeTitle")!;
            var toolRow = panel.FindControl<StackPanel>("DevelopToolRow")!;
            var scroll = panel.FindControl<ScrollViewer>("DevelopControlsScrollViewer")!;
            scroll.Offset = default;
            ShellPaneLimitsTests.Settle(window);
            var profile = panel.GetVisualDescendants().OfType<DevelopGroup>().First();

            Assert.Equal(new Rect(0, 0, 240, 209), Box(navigator, left));
            Assert.Equal(new Rect(10, 4, 220, 24), Box(navigatorHeader, left));
            Assert.Equal(new Rect(10, 32, 220, 168), Box(navigatorWell, left));
            Assert.Equal(217, Box(presets, left).Y);
            var shell = (Grid)left.Parent!;
            var seams = shell.Children.OfType<GridSplitter>()
                .Select(splitter => splitter.GetVisualDescendants().OfType<Border>().First().TranslatePoint(default, shell)!.Value.X);
            Assert.Equal(new[] { 240d, 240 + 1 + shell.ColumnDefinitions[2].ActualWidth }, seams);

            foreach (var category in presets.GetVisualDescendants().OfType<Expander>())
            {
                var name = category.GetVisualDescendants().OfType<TextBlock>().First(text => text.Classes.Contains("section-label"));
                var chevron = category.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().First(path => path.Name == "ExpandCollapseChevron");
                Assert.Equal(Middle(chevron, category), Middle(name, category), 0.5);
            }

            AssertBand(window, navigator, navigatorWell, left);
            Assert.Equal(new Rect(0, 0, panel.Bounds.Width, band.Bounds.Height), Box(band, panel));
            Assert.Equal(Box(navigatorHeader, left).Y, Box(header, panel).Y);
            Assert.Equal(24, header.Bounds.Height);
            Assert.Equal(Top(navigatorTitle, window), Top(title, window));
            AssertBand(window, band, panel.FindControl<Border>("DevelopScopeWell")!, panel);
            Assert.Equal(new Thickness(4), panel.FindControl<Border>("DevelopScopeWell")!.Padding);
            Assert.Equal(Box(band, panel).Bottom + 8, Box(toolRow, panel).Y);
            Assert.Equal(Box(toolRow, panel).Bottom + 8, Box(profile, panel).Y);

            vm.IsDevelopMode = false;
            ShellPaneLimitsTests.Settle(window);
            var review = window.FindControl<BrowseReviewPane>("BrowseReviewPane")!;
            var browseBand = review.FindControl<Border>("BrowseHistogramBox")!;
            var browseTitle = review.FindControl<TextBlock>("BrowseHistogramHeader")!;
            var content = (Control)review.GetVisualDescendants().OfType<ScrollViewer>().First().Content!;

            Assert.Equal(new Rect(0, 0, review.Bounds.Width, browseBand.Bounds.Height), Box(browseBand, review));
            Assert.Equal(new Rect(10, 4, review.Bounds.Width - 20, 24), Box((Control)browseTitle.Parent!, review));
            Assert.Equal(Top(navigatorTitle, window), Top(browseTitle, window));
            AssertBand(window, browseBand, review.FindControl<Border>("BrowseHistogramWell")!, review);
            Assert.Equal(new Thickness(4), review.FindControl<Border>("BrowseHistogramWell")!.Padding);
            Assert.Equal(Box(browseBand, review).Bottom + 8, Box(content, review).Y);
            await Task.CompletedTask;
        });
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void CurvePointsKeepTheirGrabRadius(bool gray)
    {
        var data = new CurveData();
        data.AddPointAndReturnIndex(0.3, 0.4);
        data.AddPointAndReturnIndex(0.7, 0.6);
        var curve = new CurveView { Width = 220, Height = 180, Curve = data };
        var window = new Window { Width = 250, Height = 220, Content = curve };
        using var scope = new TestUiScope(window, Theme(gray));
        Settle(window);
        var canvas = curve.FindControl<Canvas>("CurveCanvas")!;
        var rest = Points(canvas);
        Assert.All(canvas.Children.OfType<Ellipse>(), point => AssertPoint(point, 6));
        var point = data.Points[1];
        var center = new Point(point.X * canvas.Bounds.Width,
            (1 - point.Y) * canvas.Bounds.Height);
        var hover = canvas.TranslatePoint(center, window)!.Value;
        window.MouseMove(hover);
        Settle(window);
        var hovered = Points(canvas);
        AssertPoint(canvas.Children.OfType<Ellipse>().ElementAt(1), 8);
        var press = canvas.TranslatePoint(center + new Vector(9, 0), window)!.Value;
        window.MouseDown(press, MouseButton.Left);
        Settle(window);
        var dragged = Points(canvas);
        var dragIndex = typeof(CurveView).GetField("_dragPointIndex",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(curve);
        var radius = typeof(CurveView).GetField("SelectRadius",
            BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue();

        output.WriteLine("G4 " + JsonSerializer.Serialize(new
        {
            theme = Theme(gray).Key,
            channel = curve.ActiveChannel.ToString(),
            canvas = canvas.Bounds.Size.ToString(),
            rest,
            hovered,
            dragged,
            grabRadius = radius,
            pressDistance = 9,
            selectedIndex = dragIndex,
            selectedExistingPoint = Equals(dragIndex, 1) && data.Points.Count == 4,
            pointCount = data.Points.Count
        }));
        Assert.Equal(10d, radius);
        Assert.Equal(1, dragIndex);
        Assert.Equal(4, data.Points.Count);
        AssertPoint(canvas.Children.OfType<Ellipse>().ElementAt(1), 8);
        window.MouseUp(press, MouseButton.Left);
        Settle(window);
    }

    [AvaloniaTheory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public void DragMovesInnerPointAndEndpoint(bool gray, int index)
    {
        var data = new CurveData();
        data.AddPointAndReturnIndex(0.3, 0.4);
        data.AddPointAndReturnIndex(0.7, 0.6);
        var curve = new CurveView { Width = 220, Height = 180, Curve = data };
        var window = new Window { Width = 250, Height = 220, Content = curve };
        using var scope = new TestUiScope(window, Theme(gray));
        Settle(window);
        var canvas = curve.FindControl<Canvas>("CurveCanvas")!;
        var original = data.Points[index];
        var center = new Point(original.X * canvas.Bounds.Width,
            (1 - original.Y) * canvas.Bounds.Height);
        // Endpoint circles are clipped at the canvas edge; press on their visible half.
        var press = canvas.TranslatePoint(center + (index == 0 ? new Vector(1, -1) : default), window)!.Value;
        window.MouseMove(press);
        window.MouseDown(press, MouseButton.Left);
        var pathBeforeMove = canvas.Children.OfType<Avalonia.Controls.Shapes.Path>().Single();
        var target = canvas.TranslatePoint(center + new Vector(12, -16), window)!.Value;
        window.MouseMove(target);
        Settle(window);
        AssertPoint(canvas.Children.OfType<Ellipse>().ElementAt(index), 8);
        // Base 360d8e2 defers the model and path update until release; only the point moves during drag.
        Assert.Same(pathBeforeMove, canvas.Children.OfType<Avalonia.Controls.Shapes.Path>().Single());
        Assert.Equal(original, data.Points[index]);
        Capture(window, $"wp7-curve-dragged-{index}-{(gray ? "gray" : "dark")}");
        window.MouseUp(target, MouseButton.Left);
        Settle(window);
        Assert.Equal(index == 0 ? 0 : original.X + 12 / canvas.Bounds.Width, data.Points[index].X, 6);
        Assert.Equal(original.Y + 16 / canvas.Bounds.Height, data.Points[index].Y, 6);
        Assert.Equal(4, data.Points.Count);
        var pathAfterRelease = canvas.Children.OfType<Avalonia.Controls.Shapes.Path>().Single();
        Assert.NotSame(pathBeforeMove, pathAfterRelease);
        var moved = data.Points[index];
        // The displayed path samples a byte-valued LUT; allow its subpixel quantization.
        Assert.True(pathAfterRelease.Data!.StrokeContains(new Avalonia.Media.Pen(pathAfterRelease.Stroke, 4),
            new Point(moved.X * canvas.Bounds.Width, (1 - moved.Y) * canvas.Bounds.Height)));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RightClickRemovesInnerPoint(bool gray)
    {
        var data = new CurveData();
        data.AddPointAndReturnIndex(0.3, 0.4);
        data.AddPointAndReturnIndex(0.7, 0.6);
        var curve = new CurveView { Width = 220, Height = 180, Curve = data };
        var window = new Window { Width = 250, Height = 220, Content = curve };
        using var scope = new TestUiScope(window, Theme(gray));
        Settle(window);
        Capture(window, $"wp7-curve-rest-{(gray ? "gray" : "dark")}");
        var canvas = curve.FindControl<Canvas>("CurveCanvas")!;
        var target = canvas.TranslatePoint(new Point(.3 * canvas.Bounds.Width, .6 * canvas.Bounds.Height), window)!.Value;
        window.MouseDown(target, MouseButton.Right);
        window.MouseUp(target, MouseButton.Right);
        Settle(window);
        Assert.Equal(3, data.Points.Count);
        Assert.DoesNotContain(data.Points, point => point.X == .3);
    }

    private static void AssertPoint(Ellipse point, double diameter)
    {
        Assert.Equal(diameter, point.Width);
        Assert.Equal(diameter, point.Height);
        Assert.Equal(1, point.StrokeThickness);
    }

    private static void Capture(Window window, string scene)
    {
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.Equal(new PixelSize(250, 220), frame.PixelSize);
        var directory = System.IO.Path.Combine(GoldenTestPaths.RepositoryRoot, "artifacts", "shots");
        Directory.CreateDirectory(directory);
        frame.Save(System.IO.Path.Combine(directory, scene + ".png"), PngBitmapEncoderOptions.Default);
    }

    private static void AssertBand(Window window, Border band, Border well, Visual host)
    {
        Assert.Equal(ThemeColor(window, "SurfaceHigh"), ((ISolidColorBrush)band.Background!).Color);
        Assert.Equal(ThemeColor(window, "Divider"), ((ISolidColorBrush)band.BorderBrush!).Color);
        Assert.Equal(new Thickness(0, 0, 0, 1), band.BorderThickness);
        Assert.Equal(new Thickness(10, 4, 10, 8), band.Padding);
        Assert.Equal(ThemeColor(window, "SurfaceLow"), ((ISolidColorBrush)well.Background!).Color);
        Assert.Equal(Box(band, host).Bottom - 9, Box(well, host).Bottom);
    }

    private static Color ThemeColor(Window window, string key)
    {
        Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var resource));

        return ((ISolidColorBrush)resource!).Color;
    }

    private static Rect Box(Control control, Visual host) =>
        new(control.TranslatePoint(default, host)!.Value, control.Bounds.Size);

    private static double Middle(Control control, Visual host) =>
        control.TranslatePoint(new Point(0, control.Bounds.Height / 2), host)!.Value.Y;

    private static double Top(Visual control, Window window) =>
        control.TranslatePoint(default, window)!.Value.Y;

    private static ThemeVariant Theme(bool gray) =>
        gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark;

    private static object Size(Control control) => new
    {
        name = control.Name,
        width = control.Bounds.Width,
        height = control.Bounds.Height,
        visible = control.IsEffectivelyVisible,
        enabled = control.IsEffectivelyEnabled
    };

    private static object[] Points(Canvas canvas) =>
        canvas.Children.OfType<Ellipse>().Select((ellipse, index) => (object)new
        {
            index,
            width = ellipse.Width,
            height = ellipse.Height,
            drawnWidth = ellipse.Bounds.Width,
            drawnHeight = ellipse.Bounds.Height,
            stroke = ellipse.StrokeThickness
        }).ToArray();

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
