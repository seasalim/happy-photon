using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsShowcaseTests
{
    [AvaloniaTheory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, .5)]
    [InlineData(true, .5)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public void RadialBrushMeetsSpatialAndBuildGates(bool outside, double feather)
    {
        var frame = new LocalsFrame(1600, 1067, .1, .08, .8, .82);
        var size = new Size(800, 547);
        var local = new LocalAdjustment { Type = "radial", Rx = .28, Ry = .18, Angle = 37,
            Feather = feather, Outside = outside };
        var brush = LocalsOverlayControl.BuildMaskBrush(local, frame, size);
        var stopError = 0d;
        if (feather > 0) for (var i = 0; i <= 10000; i++)
        {
            var t = i / 10000d;
            var index = Math.Min(255, (int)(t * 256));
            var a = brush.GradientStops[index];
            var b = brush.GradientStops[index + 1];
            var alpha = (a.Color.A + (b.Color.A - a.Color.A) * (t * 256 - index)) / 255d;
            var weight = 1 - t * t * (3 - 2 * t);
            stopError = Math.Max(stopError, Math.Abs(alpha - (outside ? 1 - weight : weight)));
        }
        Assert.InRange(stopError, 0, .002);
        var control = new Border { Background = brush, Width = size.Width, Height = size.Height };
        control.Measure(size);
        control.Arrange(new Rect(size));
        using var bitmap = new RenderTargetBitmap(new PixelSize(800, 547), new Vector(96, 96));
        bitmap.Render(control);
        var bytes = new byte[800 * 547 * 4];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(0, 0, 800, 547), handle.AddrOfPinnedObject(), bytes.Length, 800 * 4); }
        finally { handle.Free(); }
        var spatialError = 0d;
        for (var y = 2; y < 545; y += 7) for (var x = 2; x < 798; x += 7)
        {
            var dx = (frame.CropX + (x + .5) / size.Width * frame.CropWidth - local.Cu) * frame.Width / frame.LongEdge;
            var dy = (frame.CropY + (y + .5) / size.Height * frame.CropHeight - local.Cv) * frame.Height / frame.LongEdge;
            var theta = local.Angle * Math.PI / 180;
            var a = (dx * Math.Cos(theta) + dy * Math.Sin(theta)) / local.Rx;
            var b = (-dx * Math.Sin(theta) + dy * Math.Cos(theta)) / local.Ry;
            var rho = Math.Sqrt(a * a + b * b);
            // One pixel in metric space, conservatively scaled by the shorter semi-axis.
            if (feather == 0 && Math.Abs(rho - 1) < .8 / 800 / local.Ry) continue;
            var t = feather == 0 ? (rho < 1 ? 0 : 1) : Math.Clamp((rho - 1 + feather) / feather, 0, 1);
            var weight = 1 - t * t * (3 - 2 * t);
            if (outside) weight = 1 - weight;
            spatialError = Math.Max(spatialError, Math.Abs(bytes[(y * 800 + x) * 4 + 3] / (255 * .35) - weight));
        }
        var times = new List<double>();
        for (var i = 0; i < 20; i++)
        {
            var start = Stopwatch.GetTimestamp();
            LocalsOverlayControl.BuildMaskBrush(local, frame, size);
            times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        var ms = times.Order().ElementAt(10);
        output.WriteLine($"G8 outside={outside} f={feather} stop={stopError:F6} spatial={spatialError:F6} ms={ms:F4} base reads=0");
        Assert.InRange(spatialError, 0, .02);
        Assert.True(ms < 5);
    }
}
