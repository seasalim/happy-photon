using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class CursorAnchoredZoomTests
{
    [AvaloniaFact]
    public void WheelStep_AppliesLayoutAndAnchorBeforeInputReturns()
    {
        using var bitmap = CreateBitmap(1200, 900);
        var viewer = CreateViewer(
            bitmap,
            autoFit: false,
            zoomLevel: 0.75);
        var window = Show(viewer, 500, 400);
        try
        {
            var scroll = Scroll(viewer);
            scroll.Offset = new Vector(230, 145);
            Drain();
            var image = Image(viewer);
            var pointer = ViewportPoint(window, scroll, 0.73, 0.36);
            var sizeBefore = image.Bounds.Size;
            var anchorBefore = NormalizedImagePoint(window, image, pointer);
            Vector? offsetDuringChangedLayout = null;
            viewer.LayoutUpdated += (_, _) =>
            {
                if (offsetDuringChangedLayout == null &&
                    image.Bounds.Size != sizeBefore)
                {
                    offsetDuringChangedLayout = scroll.Offset;
                }
            };

            window.MouseWheel(
                pointer,
                new Vector(0, 1),
                RawInputModifiers.None);

            Assert.NotEqual(sizeBefore, image.Bounds.Size);
            Assert.Equal(scroll.Offset, offsetDuringChangedLayout);
            AssertSameImagePoint(
                anchorBefore,
                NormalizedImagePoint(window, image, pointer),
                image);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ProvisionalWheelZoom_TrueOriginalTransitionKeepsFirstAnchor()
    {
        using var provisional = CreateBitmap(1200, 900);
        using var fresh = CreateBitmap(1800, 1350);
        var viewer = CreateViewer(provisional);
        var window = Show(viewer, 500, 400);
        try
        {
            var scroll = Scroll(viewer);
            var image = Image(viewer);
            var pointer = ViewportPoint(window, scroll, 0.72, 0.38);
            window.MouseWheel(
                pointer,
                new Vector(0, 1),
                RawInputModifiers.None);
            Drain();

            var centerBefore = NormalizedViewportCenter(scroll, image);
            var sizeBefore = image.Bounds.Size;
            var provisionalZoom = viewer.ZoomLevel;
            var originalSize = new PixelSize(6000, 4500);

            viewer.OriginalViewPixelSize = originalSize;
            viewer.UpdateLayout();
            Assert.True(image.Bounds.Width > sizeBefore.Width * 4);

            viewer.ZoomLevel = provisionalZoom *
                provisional.PixelSize.Width / originalSize.Width;
            viewer.Source = fresh;
            Drain();

            Assert.Equal(sizeBefore.Width, image.Bounds.Width, precision: 8);
            Assert.Equal(sizeBefore.Height, image.Bounds.Height, precision: 8);
            AssertSameImagePoint(
                centerBefore,
                NormalizedViewportCenter(scroll, image),
                image);
        }
        finally
        {
            window.Close();
        }
    }
}
