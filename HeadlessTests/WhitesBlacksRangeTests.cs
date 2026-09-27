using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WhitesBlacksRangeTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void GlobalPointsPreserveRangeWeightsAndShowMaskCoverage(bool raw)
    {
        const int width = 256;
        var samples = Enumerable.Range(0, width).SelectMany(i => new[] { (ushort)(i * 257), (ushort)(i * 97), (ushort)(i * 31) }).ToArray();
        using var basis = new BaseImage(RawBaseLoader.ImportRgb16(MemoryMarshal.AsBytes(samples.AsSpan()), width, 1),
            new(raw ? BaseSourceKind.RawLibRaw : BaseSourceKind.Standard, raw, BaseDecodeSettings.Default,
                null, null, 6504, 0, false, null, 1, width, 1));
        var local = new LocalAdjustment { Angle = 0, Cu = 2, Feather = .001, Whites = 70, Blacks = -70,
            Luminance = new() { Enabled = true, Lower = .4, Upper = .7, Softness = .1 },
            Hue = new() { Enabled = true, Center = 40, Width = 90, Softness = 30 } };
        var settings = new EditSettings { Locals = [local], Exposure = 1.3,
            Wb = new() { Mode = WbMode.Picked, Gains = [1.5, 1, .8] } };
        using var geometry = RenderGeometry.Apply(basis.Pixels, settings, out var trace);
        var wb = RenderChromaticStage.CreateWhiteBalanceMatrix(basis.Info, settings);
        var localOp = new WhitesBlacksOperator(local.Whites, local.Blacks, raw);
        var globalOp = new WhitesBlacksOperator(80, -80, raw);
        foreach (var active in new[] { false, true })
        {
            settings.Whites = active ? 80 : 0; settings.Blacks = active ? -80 : 0;
            var plan = RenderLocals.Create(settings, trace, width, 1, info: basis.Info)!;
            using var mask = LocalRangeMaskRenderer.Render(basis, settings, local, new PixelSize(width, 1), 0xffffff, CancellationToken.None);
            using var frame = mask.Lock();
            for (var pixel = 1; pixel < width; pixel++)
            {
                double r = samples[pixel * 3] / 65535d, g = samples[pixel * 3 + 1] / 65535d, b = samples[pixel * 3 + 2] / 65535d;
                (r, g, b) = (wb[0, 0] * r + wb[0, 1] * g + wb[0, 2] * b,
                    wb[1, 0] * r + wb[1, 1] * g + wb[1, 2] * b, wb[2, 0] * r + wb[2, 1] * g + wb[2, 2] * b);
                var lab = OklabColor.Classify(r, g, b);
                var weight = LuminanceWindow.Weight(local.Luminance, lab.L) * HueWindow.Weight(local.Hue, lab.Hue, lab.Chroma);
                Assert.Equal((byte)Math.Round(weight * .35 * 255), Marshal.ReadByte(frame.Address + pixel * 4 + 3));
                var position = WhitesBlacksOperator.Position((Rec2020Luminance.Red * r + Rec2020Luminance.Green * g + Rec2020Luminance.Blue * b) * Math.Pow(2, settings.Exposure));
                var expected = r * (active ? globalOp.Gain(position) : 1) * Math.Pow(localOp.Gain(position), weight);
                plan.ApplyColor(pixel, ref r, ref g, ref b);
                Assert.InRange(Math.Abs(r - expected), 0, 1e-12);
            }
        }
    }
}
