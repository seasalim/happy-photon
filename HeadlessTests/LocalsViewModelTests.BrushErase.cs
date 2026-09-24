using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests
{
    [AvaloniaFact]
    public async Task EraseStrokeDrawsOneUniformTranslucentBand()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.AddBrushCommand.Execute(null);
        Assert.True(vm.BeginBrushStroke(new(.15, .15))); await vm.CompleteLocalsGestureAsync();
        vm.BrushMode = "erase";
        Assert.True(vm.BeginBrushStroke(new(.3, .5)));
        // A zig-zag produces sharp joins, where a stroked widened path showed inner join fans.
        for (var i = 1; i < 12; i++) vm.ExtendBrushStroke(new(.3 + i * .04, .5 + (i % 2 == 0 ? -.08 : .08)), 1000);
        var stroke = vm.LiveBrushStroke!;
        Assert.True(stroke.Points.Count > 6);
        var overlay = new LocalsOverlayControl { DataContext = vm };
        overlay.Measure(new Size(640, 480)); overlay.Arrange(new Rect(0, 0, 640, 480));
        using var target = new RenderTargetBitmap(new PixelSize(640, 480), new Vector(96, 96));
        using (var context = target.CreateDrawingContext()) overlay.Render(context);
        var pixels = new byte[640 * 480 * 4];
        unsafe { fixed (byte* p = pixels) target.CopyPixels(new PixelRect(0, 0, 640, 480), (nint)p, pixels.Length, 640 * 4); }
        var frame = vm.LocalsFrame!.Value;
        var radius = stroke.Radius * 640 / frame.CropWidth * frame.LongEdge / frame.Width;
        Assert.True(radius > 12);
        var centers = stroke.Points.Select(point => new Point(
            (point.U / (double)LocalBrushPoint.Scale - frame.CropX) / frame.CropWidth * 640,
            (point.V / (double)LocalBrushPoint.Scale - frame.CropY) / frame.CropHeight * 480)).ToArray();
        // Inside the envelope every pixel is the same premultiplied 30 % white: no lines, no doubled joins.
        int inside = 0, off = 0;
        for (var y = 0; y < 480; y++)
        for (var x = 0; x < 640; x++)
        {
            var within = false;
            for (var s = 1; s < centers.Length && !within; s++)
                within = DistanceToSegment(new Point(x + .5, y + .5), centers[s - 1], centers[s]) < radius - 3;
            if (!within) continue;
            inside++;
            var i = (y * 640 + x) * 4;
            var a = pixels[i + 3];
            if (Math.Abs(a - 77) > 3 || Math.Abs(pixels[i] - a) > 3 || Math.Abs(pixels[i + 1] - a) > 3 || Math.Abs(pixels[i + 2] - a) > 3)
                off++;
        }
        Assert.True(inside > 1000);
        Assert.Equal(0, off);
        vm.EscapeLocals();
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        var t = Math.Clamp(Vector.Dot(p - a, ab) / Math.Max(Vector.Dot(ab, ab), 1e-9), 0, 1);
        return ((Vector)(p - (a + ab * t))).Length;
    }
}
