using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HappyPhoton.Services;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DisplayChainTraceTests
{
    [Fact]
    public void ZoomGeometry_IsDeviceTrueAndFitPreservesLogicalGeometry()
    {
        var pixels = new PixelSize(1500, 1000);
        var original = new PixelSize(6000, 4000);
        var fitBox = new Size(1034, 628);

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
        Assert.Equal(1413, ZoomGeometryCalculator.FittedDeviceLongEdge(
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
            // test-teardown-policy: allow - enclosing try/finally closes window.
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
            // test-teardown-policy: allow - enclosing try/finally closes manualWindow.
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
            // test-teardown-policy: allow - enclosing try/finally closes fitWindow.
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
        using var bitmap = CreateBitmap(1600, 800);
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
                // test-teardown-policy: allow - enclosing try/finally closes window.
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
                    Assert.Equal(1600, after.Width * 1.5, 6);
                    Assert.Equal(800, after.Height * 1.5, 6);
                }
            }
            finally
            {
                window.Close();
            }
        }
    }

    private static Bitmap CreateBitmap(int width, int height)
    {
        using var image = new MagickImage(MagickColors.Gray, (uint)width, (uint)height);
        return BitmapConversionService.ConvertToBitmap(image)!;
    }
}
