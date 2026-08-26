using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HappyPhoton.Services;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CompareLoupeMappingTests
{
    [AvaloniaFact]
    public void MixedAspectPanes_LoupeSameNormalizedPointAndRestoreExactly()
    {
        using var landscape = CreateBitmap(1600, 900);
        using var portrait = CreateBitmap(900, 1600);
        var first = new ZoomPanControl
        {
            Source = landscape,
            AutoFit = false,
            ScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden
        };
        var second = new ZoomPanControl
        {
            Source = portrait,
            AutoFit = false,
            ScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden
        };
        var host = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        host.Children.Add(first);
        Grid.SetColumn(second, 1);
        host.Children.Add(second);
        var window = new Window
        {
            Width = 800,
            Height = 500,
            Content = host
        };
        window.Show();
        Drain();

        try
        {
            first.ZoomLevel = first.GetFitZoomLevel();
            second.ZoomLevel = second.GetFitZoomLevel();
            var restore = new NormalizedViewport(
                new NormalizedPoint(0.43, 0.52),
                1);
            first.ApplyNormalizedViewport(restore);
            second.ApplyNormalizedViewport(restore);
            Drain();
            var beforeFirst = first.CaptureNormalizedViewport();
            var beforeSecond = second.CaptureNormalizedViewport();
            var loupePoint = new NormalizedPoint(0.23, 0.71);

            first.BeginSynchronizedLoupePeek(loupePoint);
            second.BeginSynchronizedLoupePeek(loupePoint);
            Drain();

            Assert.True(first.IsLoupePeekActive);
            Assert.True(second.IsLoupePeekActive);
            AssertCenter(loupePoint, first.VisibleRegion);
            AssertCenter(loupePoint, second.VisibleRegion);

            first.EndSynchronizedLoupePeek();
            second.EndSynchronizedLoupePeek();
            Drain();

            Assert.False(first.IsLoupePeekActive);
            Assert.False(second.IsLoupePeekActive);
            AssertViewport(beforeFirst, first.CaptureNormalizedViewport());
            AssertViewport(beforeSecond, second.CaptureNormalizedViewport());
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertCenter(NormalizedPoint expected, Rect? region)
    {
        var actual = Assert.IsType<Rect>(region);
        Assert.InRange(Math.Abs(actual.Center.X - expected.X), 0, 0.01);
        Assert.InRange(Math.Abs(actual.Center.Y - expected.Y), 0, 0.01);
    }

    private static void AssertViewport(
        NormalizedViewport expected,
        NormalizedViewport actual)
    {
        Assert.InRange(Math.Abs(expected.Center.X - actual.Center.X), 0, 0.01);
        Assert.InRange(Math.Abs(expected.Center.Y - actual.Center.Y), 0, 0.01);
        Assert.InRange(
            Math.Abs(expected.ZoomRelativeToFit - actual.ZoomRelativeToFit),
            0,
            0.01);
    }

    private static Avalonia.Media.Imaging.Bitmap CreateBitmap(int width, int height)
    {
        using var image = new MagickImage(
            MagickColors.DarkSlateGray,
            (uint)width,
            (uint)height);
        return BitmapConversionService.ConvertToBitmap(image)!;
    }

    private static void Drain()
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }
}
