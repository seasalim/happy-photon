using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class AlignmentGridOverlayTests
{
    [AvaloniaFact]
    public void SharedDrawingEmitsExpectedLinesAndCropKeepsRuleOfThirds()
    {
        var canvas = new Canvas();
        OverlayGridLines.Draw(
            canvas,
            new Rect(0, 0, 300, 180),
            HappyPhotonColors.CropGridLine,
            3,
            3);
        Assert.Equal(4, canvas.Children.OfType<Line>().Count());

        var crop = new CropOverlayControl
        {
            Width = 300,
            Height = 180,
            Crop = new CropRegion()
        };
        var window = Show(crop, 300, 180);
        try
        {
            var cropCanvas = crop.FindControl<Canvas>("OverlayCanvas")!;
            Assert.Equal(4, cropCanvas.Children.OfType<Line>().Count());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void OverlaySharesImageBoundsAndTracksVisibilityAndZoom()
    {
        using var source = new WriteableBitmap(
            new PixelSize(400, 200),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);
        var viewer = new ZoomPanControl
        {
            Source = source,
            AutoFit = false,
            ZoomLevel = 0.5,
            ShowAlignmentGrid = true
        };
        var window = Show(viewer, 500, 300);
        try
        {
            var imagePanel = viewer.FindControl<Panel>("ImagePanel")!;
            var image = viewer.FindControl<Image>("ImageControl")!;
            var overlay = viewer.FindControl<AlignmentGridOverlayControl>(
                "AlignmentGridOverlay")!;
            var canvas = overlay.FindControl<Canvas>("GridCanvas")!;

            Assert.Same(imagePanel, image.Parent);
            Assert.Same(imagePanel, overlay.Parent);
            Assert.Equal(image.Bounds, overlay.Bounds);
            Assert.False(overlay.IsHitTestVisible);
            Assert.True(overlay.IsGridVisible);
            Assert.Contains("visible", canvas.Classes);
            Assert.Equal(22, canvas.Children.OfType<Line>().Count());

            viewer.ZoomLevel = 1;
            Drain();
            Assert.Equal(image.Bounds, overlay.Bounds);
            Assert.Equal(new Size(400, 200), overlay.Bounds.Size);

            viewer.IsCropMode = true;
            Assert.False(overlay.IsGridVisible);
            Assert.DoesNotContain("visible", canvas.Classes);

            viewer.IsCropMode = false;
            Assert.True(overlay.IsGridVisible);
            viewer.ShowAlignmentGrid = false;
            Assert.False(overlay.IsGridVisible);
        }
        finally
        {
            window.Close();
        }
    }

    private static Window Show(Control content, double width, double height)
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = content
        };
        window.Show();
        Drain();
        return window;
    }

    private static void Drain()
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }
}
