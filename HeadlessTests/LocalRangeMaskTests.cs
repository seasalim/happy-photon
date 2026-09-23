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
    public void HuePickMatchesSmallerSurfaceClassificationForAlternatingColors()
    {
        const int width = 1000, height = 100;
        var colors = new[] { new Oklch(.65, .15, 0), new Oklch(.65, .15, 2 * Math.PI / 3) };
        var values = new ushort[width * height * 3];
        for (var i = 0; i < width * height; i++)
        {
            var rgb = OklabColor.ToLinearRec2020(colors[i % 2]);
            values[i * 3] = (ushort)Math.Round(rgb.Red * 65535);
            values[i * 3 + 1] = (ushort)Math.Round(rgb.Green * 65535);
            values[i * 3 + 2] = (ushort)Math.Round(rgb.Blue * 65535);
        }
        using var basis = new BaseImage(RawBaseLoader.ImportRgb16(MemoryMarshal.AsBytes(values.AsSpan()), width, height),
            new(BaseSourceKind.Standard, false, BaseDecodeSettings.Default, null, null, 6504, 0, false, null, 1, width, height));
        var size = new PixelSize(100, 10);
        var point = new Point(.505, .55);
        var full = LocalRangeSampling.Pick(basis, new(), point, new(width, height));
        Assert.Equal("mixed colors", full.Rejection);
        using var resized = RenderGeometry.Apply(basis.Pixels, new(), out _);
        resized.Resize(new MagickGeometry((uint)size.Width, (uint)size.Height) { IgnoreAspectRatio = true });
        using var pixels = resized.GetPixels();
        var rgbAtClick = pixels.GetArea(50, 5, 1, 1)!;
        var lab = OklabColor.Classify(rgbAtClick[0] / 65535d, rgbAtClick[1] / 65535d, rgbAtClick[2] / 65535d);
        var local = new LocalAdjustment { Angle = 0, Cu = 2,
            Hue = new() { Enabled = true, Center = lab.Hue, Width = .01, Softness = 0 } };
        using var mask = LocalRangeMaskRenderer.Render(basis, new(), local, size, 0xffffff, CancellationToken.None);
        using var frame = mask.Lock();
        Assert.Equal((byte)Math.Round(HueWindow.Reliability(lab.Chroma) * .35 * 255),
            Marshal.ReadByte(frame.Address + 5 * frame.RowBytes + 50 * 4 + 3));
        var pick = LocalRangeSampling.Pick(basis, new(), point, size);
        Assert.Null(pick.Rejection);
        Assert.Equal(1, pick.Count);
        Assert.InRange(Math.Abs(Math.IEEERemainder(pick.Hue!.Value - lab.Hue, 360)), 0, 1e-10);
    }
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
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void HueMaskMultipliesLuminanceAndIgnoresHueOnMonochrome(bool monochrome)
    {
        var pixels = new MagickImage(monochrome ? MagickColors.Gray : MagickColors.Red, 64, 48) { ColorSpace = ColorSpace.RGB };
        using var basis = new BaseImage(pixels, new(BaseSourceKind.RawLibRaw, true, BaseDecodeSettings.Default,
            null, null, 6504, 0, false, null, 1, 64, 48) { IsMonochrome = monochrome });
        using var source = pixels.GetPixels();
        var before = source.GetArea(0, 0, 64, 48)!;
        var local = new LocalAdjustment { Enabled = false, Angle = 0, Cu = 2,
            Luminance = new() { Enabled = true, Lower = .4, Upper = .8, Softness = .1 },
            Hue = new() { Enabled = true, Center = 30, Width = 30, Softness = 10 } };
        var settings = new EditSettings { Locals = [local] };
        using var mask = LocalRangeMaskRenderer.Render(basis, settings, local, new PixelSize(64, 48),
            0xffffff, CancellationToken.None);
        using var frame = mask.Lock();
        var first = new byte[4]; Marshal.Copy(frame.Address, first, 0, 4);
        var lab = OklabColor.Classify(before[0] / 65535d, before[1] / 65535d, before[2] / 65535d);
        var weight = LuminanceWindow.Weight(local.Luminance, lab.L) *
            (monochrome ? 1 : HueWindow.Weight(local.Hue, lab.Hue, lab.Chroma));
        Assert.Equal((byte)Math.Round(weight * .35 * 255), first[3]);
        Assert.Equal(before, source.GetArea(0, 0, 64, 48));
    }

}
