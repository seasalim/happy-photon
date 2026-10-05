using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
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
        Assert.Equal(20, header.Bounds.Height);
        Assert.All(new[] { "HistogramScopeButton", "RawHistogramScopeButton", "WaveformScopeButton" },
            name => Assert.Equal(new Size(20, 20), selector.FindControl<ToggleButton>(name)!.Bounds.Size));
        Assert.All(new[] { "DisplayFloorTriangleTarget", "SceneHighlightTriangleTarget" },
            name => Assert.Equal(new Size(20, 20), histogram.FindControl<ToggleButton>(name)!.Bounds.Size));
        panel.DataContext = null;
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
