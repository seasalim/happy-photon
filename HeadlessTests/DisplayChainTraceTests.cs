using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DisplayChainTraceTests
{
    [Fact]
    public void Calculator_AccountsForNonUnitRenderScaling()
    {
        var mapping = DisplayChainMappingCalculator.Calculate(
            new PixelSize(800, 400),
            new Size(400, 200),
            new Size(600, 500),
            1.5);

        Assert.Equal(new Rect(0, 0, 600, 300), mapping.DeviceRectangle);
        Assert.Equal(0.75, mapping.NetScaleX);
        Assert.Equal(0.75, mapping.NetScaleY);
        Assert.False(mapping.IsOneToOne);
    }

    [AvaloniaFact]
    public void RealizedControl_ReportsEveryMappingFieldAndCoalescesChanges()
    {
        var lines = new List<string>();
        using var trace = ImageServiceHelpers.OverrideDisplayTraceForTesting(
            enabled: true,
            lines.Add);
        using var first = CreateBitmap(800, 400);
        using var sameSize = CreateBitmap(800, 400);
        var control = new ZoomPanControl
        {
            Source = first,
            ZoomLevel = 0.5,
            IsDisplayTraceActive = true
        };
        var window = new Window
        {
            Width = 600,
            Height = 500,
            Content = control
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(
                MappingLine(800, 400, 400, 200, 600, 500, 1, 400, 200, 0.5),
                Assert.Single(MappingLines(lines)));

            window.SetRenderScaling(1.5);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(
                MappingLine(800, 400, 400, 200, 600, 500, 1.5, 600, 300, 0.75),
                MappingLines(lines)[1]);

            control.Source = sameSize;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, MappingLines(lines).Count);
            Assert.Equal(MappingLines(lines)[1], MappingLines(lines)[2]);

            control.ZoomLevel = 1;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(
                MappingLine(800, 400, 800, 400, 600, 500, 1.5, 1200, 600, 1.5),
                MappingLines(lines)[3]);

            window.Height = 550;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(
                MappingLine(800, 400, 800, 400, 600, 550, 1.5, 1200, 600, 1.5),
                MappingLines(lines)[4]);

            control.ZoomLevel = 2.0 / 3;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(
                MappingLine(
                    800, 400, 533.333333, 266.666667, 600, 550,
                    1.5, 800, 400, 1, oneToOne: true),
                MappingLines(lines)[5]);
            Assert.Equal(6, MappingLines(lines).Count);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DisabledGate_DoesNotCalculateAcrossAnyMappingTrigger()
    {
        var lines = new List<string>();
        var calculations = 0;
        using var trace = ImageServiceHelpers.OverrideDisplayTraceForTesting(
            enabled: false,
            lines.Add);
        using var observer = DisplayChainTrace.OverrideCalculationObserverForTesting(
            () => calculations++);
        using var first = CreateBitmap(40, 20);
        using var second = CreateBitmap(40, 20);
        var control = new ZoomPanControl
        {
            Source = first,
            IsDisplayTraceActive = true
        };
        var window = new Window
        {
            Width = 200,
            Height = 100,
            Content = control
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            control.Source = second;
            control.ZoomLevel = 2;
            window.Width = 220;
            window.SetRenderScaling(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, calculations);
            Assert.Empty(lines);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ActiveSurface_TracksDevelopAndFullscreenTransitionsExactly()
    {
        var lines = new List<string>();
        using var trace = ImageServiceHelpers.OverrideDisplayTraceForTesting(
            enabled: true,
            lines.Add);
        using var bitmap = CreateBitmap(80, 40);
        using var root = new DisplayTraceTemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        await using var viewModel = new MainWindowViewModel(catalog);
        var develop = CreateBoundSurface(
            bitmap,
            nameof(MainWindowViewModel.IsDevelopPreviewSurfaceActive));
        var fullScreen = CreateBoundSurface(
            bitmap,
            nameof(MainWindowViewModel.IsFullScreenPreviewSurfaceActive));
        var window = new Window
        {
            Width = 300,
            Height = 200,
            DataContext = viewModel,
            Content = new Grid { Children = { develop, fullScreen } }
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(MappingLines(lines));

            viewModel.IsDevelopMode = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Single(MappingLines(lines));

            viewModel.IsFullScreenMode = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, MappingLines(lines).Count);

            viewModel.IsFullScreenMode = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, MappingLines(lines).Count);

            viewModel.IsDevelopMode = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, MappingLines(lines).Count);

            viewModel.IsFullScreenMode = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(4, MappingLines(lines).Count);

            viewModel.IsFullScreenMode = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(4, MappingLines(lines).Count);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task PreviewSwaps_ReportAllProductionProvenanceLabels()
    {
        var lines = new List<string>();
        using var trace = ImageServiceHelpers.OverrideDisplayTraceForTesting(
            enabled: true,
            lines.Add);
        using var root = new DisplayTraceTemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        await using var viewModel = new MainWindowViewModel(catalog);
        var cached = CreateBitmap(4, 3);
        var fresh = CreateBitmap(5, 2);
        var refresh = CreateBitmap(6, 4);
        var image = new ImageFile(Path.Combine(root.Path, "image.jpg"));
        viewModel.SelectedImage = image;
        viewModel.IsDevelopMode = true;

        viewModel.ReplacePreviewImage(cached, PreviewPaintSource.CachedJpeg);
        viewModel.ReplacePreviewImage(fresh, PreviewPaintSource.FreshRender);
        viewModel.ApplyPreviewRefresh(
            image,
            refresh,
            new HistogramData(),
            hasHistogram: false,
            rawHistogram: null,
            generation: 1);

        Assert.Equal(
            [
                "[DisplayChain] paint source=cached-jpeg bitmap=4x3",
                "[DisplayChain] paint source=fresh-render bitmap=5x2",
                "[DisplayChain] paint source=background-refresh bitmap=6x4"
            ],
            lines);
    }

    private static ZoomPanControl CreateBoundSurface(
        Bitmap bitmap,
        string activeProperty)
    {
        var control = new ZoomPanControl
        {
            Source = bitmap,
            ZoomLevel = 0.5
        };
        control.Bind(
            ZoomPanControl.IsDisplayTraceActiveProperty,
            new Binding(activeProperty));
        return control;
    }

    private static Bitmap CreateBitmap(int width, int height)
    {
        using var image = new MagickImage(MagickColors.Gray, (uint)width, (uint)height);
        return BitmapConversionService.ConvertToBitmap(image)!;
    }

    private static List<string> MappingLines(List<string> lines) =>
        lines.Where(line => line.StartsWith(
            "[DisplayChain] mapping ",
            StringComparison.Ordinal)).ToList();

    private static string MappingLine(
        int bitmapWidth,
        int bitmapHeight,
        double logicalWidth,
        double logicalHeight,
        double viewportWidth,
        double viewportHeight,
        double renderScaling,
        double deviceWidth,
        double deviceHeight,
        double netScale,
        bool oneToOne = false) =>
        $"[DisplayChain] mapping bitmap={bitmapWidth}x{bitmapHeight} " +
        $"logical={F(logicalWidth)}x{F(logicalHeight)} " +
        $"viewport={F(viewportWidth)}x{F(viewportHeight)} " +
        $"renderScaling={F(renderScaling)} " +
        $"deviceRect=0,0,{F(deviceWidth)},{F(deviceHeight)} " +
        $"netScale={F(netScale)}x{F(netScale)} " +
        $"oneToOne={oneToOne.ToString().ToLowerInvariant()}";

    private static string F(double value) =>
        value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    private sealed class DisplayTraceTemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"happy-photon-display-trace-{Guid.NewGuid():N}");

        public DisplayTraceTemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
