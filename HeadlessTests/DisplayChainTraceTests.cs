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

    [Fact]
    public void Calculator_TreatsSubHalfPercentRoundingAsOneToOne()
    {
        var mapping = DisplayChainMappingCalculator.Calculate(
            new PixelSize(1000, 1000),
            new Size(995, 1005),
            new Size(1000, 1000),
            1);

        Assert.True(mapping.IsOneToOne);
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
                MappingLine(
                    800, 400, 266.666667, 133.333333, 600, 500,
                    1.5, 400, 200, 0.5),
                MappingLines(lines)[1]);

            control.Source = sameSize;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, MappingLines(lines).Count);
            Assert.Equal(MappingLines(lines)[1], MappingLines(lines)[2]);

            control.ZoomLevel = 1;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(
                MappingLine(
                    800, 400, 533.333333, 266.666667, 600, 500,
                    1.5, 800, 400, 1, oneToOne: true),
                MappingLines(lines)[3]);

            window.Height = 550;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(
                MappingLine(
                    800, 400, 533.333333, 266.666667, 600, 550,
                    1.5, 800, 400, 1, oneToOne: true),
                MappingLines(lines)[4]);

            control.ZoomLevel = 2.0 / 3;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(
                MappingLine(
                    800, 400, 356, 178, 600, 550,
                    1.5, 534, 267, 0.6675),
                MappingLines(lines)[5]);
            Assert.Equal(6, MappingLines(lines).Count);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ZoomGeometry_IsDeviceTrueAndFitPreservesLogicalGeometry()
    {
        var pixels = new PixelSize(1500, 1000);
        var original = new PixelSize(6000, 4000);
        var fitBox = new Size(2068, 1256);

        Assert.Equal(4, ZoomGeometryCalculator.BitmapRelativeZoom(
            pixels, original, 1));
        Assert.Equal(6000, ZoomGeometryCalculator.RequiredDeviceLongEdge(
            original, 1));
        Assert.Equal(
            new Size(1000, 666.6666666666666),
            ZoomGeometryCalculator.ImageLogicalSize(pixels, 1, 1.5));

        var fitAtOne = ZoomGeometryCalculator.FitZoomLevel(
            pixels,
            fitBox,
            1);
        var fitAtOnePointFive = ZoomGeometryCalculator.FitZoomLevel(
            pixels,
            fitBox,
            1.5);
        var logicalAtOne = ZoomGeometryCalculator.ImageLogicalSize(
            pixels,
            fitAtOne,
            1);
        var logicalAtOnePointFive = ZoomGeometryCalculator.ImageLogicalSize(
            pixels,
            fitAtOnePointFive,
            1.5);

        Assert.Equal(logicalAtOne.Width, logicalAtOnePointFive.Width, 10);
        Assert.Equal(logicalAtOne.Height, logicalAtOnePointFive.Height, 10);
        Assert.Equal(2826, ZoomGeometryCalculator.FittedDeviceLongEdge(
            pixels,
            fitBox,
            1.5));
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
    public void SourceSwap_PreservesFitAndManualSceneGeometryAtScalingOnePointFive()
    {
        using var first = CreateBitmap(1600, 800);
        using var large = CreateBitmap(2826, 1413);
        var originalViewSize = new PixelSize(6000, 3000);

        var manual = new ZoomPanControl
        {
            Source = first,
            OriginalViewPixelSize = originalViewSize,
            ZoomLevel = 0.5,
            AutoFit = false
        };
        var requiredLongEdge = 0;
        manual.RequiredDeviceLongEdgeChanged +=
            (_, value) => requiredLongEdge = value;
        var manualWindow = new Window
        {
            Width = 400,
            Height = 300,
            Content = manual
        };

        try
        {
            manualWindow.Show();
            manualWindow.SetRenderScaling(1.5);
            Dispatcher.UIThread.RunJobs();
            var image = manual.FindControl<Image>("ImageControl")!;
            var before = image.Bounds.Size;

            manual.Source = large;
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0.5, manual.ZoomLevel, 10);
            Assert.Equal(before.Width, image.Bounds.Width, 6);
            Assert.Equal(before.Height, image.Bounds.Height, 6);
            manual.ZoomLevel = 0.75;
            Assert.Equal(4500, requiredLongEdge);
        }
        finally
        {
            manualWindow.Close();
        }

        var fitted = new ZoomPanControl
        {
            Source = first,
            OriginalViewPixelSize = originalViewSize,
            AutoFit = true
        };
        fitted.AutoFitRequested += (_, zoom) => fitted.ZoomLevel = zoom;
        var fitWindow = new Window
        {
            Width = 600,
            Height = 500,
            Content = fitted
        };

        try
        {
            fitWindow.Show();
            fitWindow.SetRenderScaling(1.5);
            Dispatcher.UIThread.RunJobs();
            var image = fitted.FindControl<Image>("ImageControl")!;
            var before = image.Bounds.Size;

            fitted.Source = large;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(before.Width, image.Bounds.Width, 6);
            Assert.Equal(before.Height, image.Bounds.Height, 6);
        }
        finally
        {
            fitWindow.Close();
        }
    }

    [AvaloniaFact]
    public void ScalingTransition_RecomputesFitAndManualGeometry()
    {
        using var bitmap = CreateBitmap(800, 400);
        foreach (var autoFit in new[] { false, true })
        {
            var control = new ZoomPanControl
            {
                Source = bitmap,
                ZoomLevel = 1,
                AutoFit = autoFit
            };
            control.AutoFitRequested += (_, zoom) => control.ZoomLevel = zoom;
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
                var image = control.FindControl<Image>("ImageControl")!;
                var before = image.Bounds.Size;

                window.SetRenderScaling(1.5);
                Dispatcher.UIThread.RunJobs();
                var after = image.Bounds.Size;

                if (autoFit)
                {
                    Assert.Equal(before.Width, after.Width, 6);
                    Assert.Equal(before.Height, after.Height, 6);
                }
                else
                {
                    Assert.Equal(1, control.ZoomLevel);
                    Assert.Equal(800, after.Width * 1.5, 6);
                    Assert.Equal(400, after.Height * 1.5, 6);
                }
            }
            finally
            {
                window.Close();
            }
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
        using var root = new TemporaryDirectory();
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
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        await using var viewModel = new MainWindowViewModel(catalog);
        var cached = CreateBitmap(4, 3);
        var fresh = CreateBitmap(5, 2);
        var refresh = CreateBitmap(6, 4);
        var resting = CreateBitmap(8, 5);
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
            generation: viewModel.LatestPreviewOutcomeGeneration);
        viewModel.ReplacePreviewImage(
            resting,
            PreviewPaintSource.RestingRender);

        var prefixes = new[]
        {
            "[DisplayChain] paint source=cached-jpeg bitmap=4x3 luma=",
            "[DisplayChain] paint source=fresh-render bitmap=5x2 luma=",
            "[DisplayChain] paint source=background-refresh bitmap=6x4 luma=",
            "[DisplayChain] paint source=resting-render bitmap=8x5 luma="
        };
        Assert.Equal(prefixes.Length, lines.Count);
        for (var index = 0; index < prefixes.Length; index++)
        {
            Assert.StartsWith(prefixes[index], lines[index]);
            Assert.Contains(" decode=", lines[index]);
            Assert.Contains(" settings=", lines[index]);
        }
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
}
