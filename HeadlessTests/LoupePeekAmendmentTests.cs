using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LoupePeekTests
{
    [AvaloniaFact]
    public void ManualZoomPeek_RestoresZoomAndViewportExactly()
    {
        var clock = new TestTimeProvider();
        using var bitmap = CreateBitmap(2400, 1800);
        var viewer = CreateViewer(clock, bitmap, new object());
        viewer.OriginalViewPixelSize = bitmap.PixelSize;
        var window = Show(viewer, 500, 400);
        try
        {
            viewer.AutoFit = false;
            viewer.ZoomLevel = 0.5;
            Drain();
            var scroll = Scroll(viewer);
            scroll.Offset = new Vector(220, 170);
            Drain();
            var zoom = viewer.ZoomLevel;
            var size = Image(viewer).Bounds.Size;
            var offset = scroll.Offset;
            var pointer = Center(viewer, window);

            Engage(window, clock, pointer);
            Assert.True(viewer.IsLoupePeekActive);
            window.MouseMove(
                pointer + new Vector(24, 18),
                RawInputModifiers.LeftMouseButton);
            Drain();
            window.MouseUp(
                pointer + new Vector(24, 18),
                MouseButton.Left,
                RawInputModifiers.None);
            Drain();

            Assert.False(viewer.IsLoupePeekActive);
            Assert.False(viewer.AutoFit);
            Assert.Equal(zoom, viewer.ZoomLevel);
            Assert.Equal(size, Image(viewer).Bounds.Size);
            Assert.Equal(offset.X, scroll.Offset.X, precision: 8);
            Assert.Equal(offset.Y, scroll.Offset.Y, precision: 8);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MoveBeforeHoldThreshold_ContinuesAsOrdinaryPan()
    {
        var clock = new TestTimeProvider();
        using var bitmap = CreateBitmap(2400, 1800);
        var viewer = CreateViewer(clock, bitmap, new object());
        viewer.OriginalViewPixelSize = bitmap.PixelSize;
        var window = Show(viewer, 500, 400);
        try
        {
            viewer.AutoFit = false;
            viewer.ZoomLevel = 0.5;
            Drain();
            var scroll = Scroll(viewer);
            scroll.Offset = new Vector(220, 170);
            Drain();
            var before = scroll.Offset;
            var pointer = Center(viewer, window);

            window.MouseDown(pointer, MouseButton.Left, RawInputModifiers.None);
            var moved = pointer + new Vector(20, 12);
            window.MouseMove(moved, RawInputModifiers.LeftMouseButton);
            Drain();
            clock.Advance(TimeSpan.FromSeconds(1));
            Drain();

            Assert.False(viewer.IsLoupePeekActive);
            Assert.Equal(before.X - 20, scroll.Offset.X, precision: 8);
            Assert.Equal(before.Y - 12, scroll.Offset.Y, precision: 8);
            window.MouseUp(moved, MouseButton.Left, RawInputModifiers.None);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Cursor_TracksLoupeEligibilityWithoutOverridingOtherModes()
    {
        var clock = new TestTimeProvider();
        using var bitmap = CreateBitmap(1200, 900);
        var viewer = CreateViewer(clock, bitmap, new object());
        var window = Show(viewer, 500, 400);
        try
        {
            var loupeCursor = viewer.Cursor;
            Assert.NotSame(Cursor.Default, loupeCursor);

            viewer.AutoFit = false;
            viewer.ZoomLevel = 1;
            Assert.Same(Cursor.Default, viewer.Cursor);

            viewer.ZoomLevel = 0.75;
            Assert.Same(loupeCursor, viewer.Cursor);

            viewer.IsCropMode = true;
            Assert.Same(Cursor.Default, viewer.Cursor);

            viewer.IsCropMode = false;
            Assert.Same(loupeCursor, viewer.Cursor);

            viewer.IsWhiteBalancePicking = true;
            Assert.NotSame(loupeCursor, viewer.Cursor);
            Assert.NotSame(Cursor.Default, viewer.Cursor);

            viewer.IsWhiteBalancePicking = false;
            viewer.Source = null;
            Assert.Same(Cursor.Default, viewer.Cursor);
        }
        finally
        {
            window.Close();
        }
    }
}
