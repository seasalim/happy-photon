using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LocalBrushMaskTests
{
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void BrushMaskMatchesRadialWithCropAndOptionalRanges(bool restricted)
    {
        using var basis = new BaseImage(new MagickImage(MagickColors.Orange, 120, 80),
            new(BaseSourceKind.Standard, false, BaseDecodeSettings.Default, null, null, 6504, 0, false, null, 1, 120, 80));
        var local = new LocalAdjustment { Type = "brush", Enabled = false,
            Strokes = [new() { Points = [new(8192, 8192)], Radius = .25, Feather = .5 }],
            Luminance = restricted ? new() { Enabled = true, Lower = .1, Upper = .9, Softness = .1 } : null,
            Hue = restricted ? new() { Enabled = true, Center = 30, Width = 180, Softness = 30 } : null };
        var settings = new EditSettings { Crop = new() { Left = .1, Top = .2, Right = .9, Bottom = .85 } };
        var radial = local with { Type = "radial", Rx = .25, Ry = .25, Feather = .5, Angle = 0, Strokes = null };
        using var mask = LocalRangeMaskRenderer.Render(basis, settings, local, new PixelSize(60, 40), 0xffffff, CancellationToken.None);
        using var reference = LocalRangeMaskRenderer.Render(basis, settings, radial, new PixelSize(60, 40), 0xffffff, CancellationToken.None);
        Assert.Equal(Bytes(reference), Bytes(mask));
        Assert.Contains(Bytes(mask), value => value > 0);
        local.Strokes = [];
        using var empty = LocalRangeMaskRenderer.Render(basis, settings, local, new PixelSize(60, 40), 0xffffff, CancellationToken.None);
        Assert.All(Bytes(empty), value => Assert.Equal(0, value));
    }
    private static byte[] Bytes(Avalonia.Media.Imaging.WriteableBitmap bitmap)
    {
        using var frame = bitmap.Lock();
        var bytes = new byte[frame.RowBytes * frame.Size.Height];
        Marshal.Copy(frame.Address, bytes, 0, bytes.Length);
        return bytes;
    }
}
