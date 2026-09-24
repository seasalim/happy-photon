using Avalonia;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

public sealed partial class LocalRangeOverlayGateTests
{
    // The same R8 geometry, C8 edits, and L x H windows as QualifiedRangeHueAgreement.
    private static EditSettings EightRangeSettings() => new()
    {
        Locals = Enumerable.Range(0, 8).Select(i => new LocalAdjustment
        {
            Type = "radial", Ordinal = i + 1, Cu = .2 * (i % 4 + 1), Cv = (i / 4 + 1) / 3d,
            Rx = .11, Ry = .11, Angle = 0, Feather = .5, Exposure = 2,
            Temperature = 50, Tint = -50, Saturation = 100,
            Luminance = new() { Enabled = true, Lower = i * .08, Upper = .4 + i * .08, Softness = i % 3 == 0 ? 0 : .1 },
            Hue = new() { Enabled = true, Center = i * 45, Width = i == 7 ? 360 : 60, Softness = i % 3 == 0 ? 0 : 30 }
        }).ToList()
    };

    private LocalAdjustment LargestSupport(BaseImage basis, EditSettings settings, PixelSize size)
    {
        // Select by positive double weight, before overlay alpha quantization; outside timing.
        using var geometry = LocalRangeSampling.Prepare(basis, settings, size, out var trace, out var wb, out _);
        DcpHueSatRenderer.Apply(geometry, basis.Info.DcpProfile?.HueSatMap);
        using var pixels = geometry.GetPixels();
        var values = pixels.GetArea(0, 0, geometry.Width, geometry.Height)!;
        var selected = settings.Locals![0];
        var largest = -1;
        foreach (var local in settings.Locals)
        {
            var neutral = settings.Clone();
            neutral.Locals = [local with { Exposure = 1, Temperature = 0, Tint = 0, Saturation = 0, Luminance = null, Hue = null }];
            var mask = RenderLocals.Create(neutral, trace, size.Width, size.Height)!;
            var count = 0;
            for (var pixel = 0; pixel < size.Width * size.Height; pixel++)
            {
                if (mask.Gain(pixel) == 1) continue;
                var o = pixel * 3;
                var r = values[o] / 65535d; var g = values[o + 1] / 65535d; var b = values[o + 2] / 65535d;
                var lab = OklabColor.Classify(wb.Row0(r, g, b), wb.Row1(r, g, b), wb.Row2(r, g, b));
                if (LuminanceWindow.Weight(local.Luminance!, lab.L) * HueWindow.Weight(local.Hue!, lab.Hue, lab.Chroma) > 0) count++;
            }
            output.WriteLine($"LH8 local={local.Ordinal} support_pixels={count} frame_pixels={size.Width * size.Height}");
            if (count > largest) { largest = count; selected = local; }
        }
        output.WriteLine($"LH8 selected={selected.Ordinal} largest_support_pixels={largest} support_scan_outside_timing=True");
        return selected;
    }
}
