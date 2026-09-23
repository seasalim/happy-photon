using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LocalRangeMaskTests
{
    [AvaloniaFact]
    public void NativeReadsPreserveBaseAndDisabledNeutralMaskHasCompleteWeight()
    {
        var pixels = new MagickImage(MagickColors.Gray, 64, 48) { ColorSpace = ColorSpace.RGB };
        using var basis = new BaseImage(pixels, new(BaseSourceKind.Standard, false, BaseDecodeSettings.Default,
            null, null, 6504, 0, false, null, 1, 64, 48));
        using var source = pixels.GetPixels();
        var before = source.GetArea(0, 0, 64, 48)!;
        var range = new LuminanceRange { Enabled = true, Lower = .8, Softness = .1 };
        var local = new LocalAdjustment { Enabled = false, Angle = 0, Cu = 2, Luminance = range };
        var settings = new EditSettings { Locals = [local] };
        var color = HappyPhotonColors.LocalMaskColor;
        using var mask = LocalRangeMaskRenderer.Render(basis, settings, local, new PixelSize(64, 48),
            (uint)(color.R << 16 | color.G << 8 | color.B), CancellationToken.None);
        using var frame = mask.Lock();
        var first = new byte[4]; Marshal.Copy(frame.Address, first, 0, 4);
        var l = OklabColor.ClassifyLightness(before[0] / 65535d, before[1] / 65535d, before[2] / 65535d);
        Assert.Equal((byte)Math.Round(LuminanceWindow.Weight(range, l) * .35 * 255), first[3]);
        Assert.Equal(before, source.GetArea(0, 0, 64, 48));
    }
}
