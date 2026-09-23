using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsContractPrototypeTests
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(false, false, false, true)]
    public void ProductionLuminanceMatchesIndependentOracle(bool isRaw, bool dcp, bool mono, bool hue = false)
    {
        var random = new Random(262);
        var source = Enumerable.Range(0, 64 * 32 * 3).Select(_ => (ushort)random.Next(65536)).ToArray();
        if (mono) for (var i = 0; i < source.Length; i += 3) source[i + 1] = source[i + 2] = source[i];
        using var basis = RenderPipelineTestSupport.CreateBase(source, isRaw, 32, isMonochrome: mono);
        var max = 0;
        foreach (var mode in Enum.GetValues<WbMode>())
        foreach (var color in mono ? new[] { false } : new[] { false, true })
        {
            var settings = new EditSettings { Wb = new() { Mode = mode, Kelvin = 8500, Tint = 30, Gains = [1.8, 1, .6] } };
            var (kg, tg) = mode switch
            {
                WbMode.Custom or WbMode.Preset => (8500d, 30d),
                WbMode.Picked => WhiteBalanceModel.EstimateFromGains(settings.Wb.Gains),
                _ => (basis.Info.AsShotKelvin, basis.Info.AsShotTint)
            };
            settings.Locals = Enumerable.Range(0, 8).Select(i => new LocalAdjustment
            {
                Ordinal = i + 1, Angle = 0, Cu = .3 + i * .1, Feather = .5,
                Exposure = i % 2 == 0 ? 1 : -1, Temperature = color ? 50 : 0,
                Tint = color ? -50 : 0, Saturation = color ? 100 : 0,
                Hue = hue ? new() { Enabled = true, Center = (350 + i * 40) % 360, Width = i == 7 ? 360 : 60, Softness = i % 3 == 0 ? 0 : 30 } : null,
                Luminance = new() { Enabled = true, Lower = i * .08, Upper = .4 + i * .08, Softness = i % 3 == 0 ? 0 : .1 }
            }).ToList();
            var raw = Raw(-2) with { Contrast = 25, Highlights = -40, Shadows = 15 };
            var standard = Standard(-2) with { Brightness = 20, BaseLookEnabled = true, Highlights = -40 };
            var wb = RenderChromaticStage.CreateWhiteBalanceMatrix(basis.Info, settings);
            using var image = RenderGeometry.Apply(basis.Pixels, settings, out var trace);
            var locals = RenderLocals.Create(settings, trace, 64, 32, info: basis.Info)!;
            var map = dcp ? HueSatMap() : null;
            using var referenceInput = new ImageMagick.MagickImage(image);
            DcpHueSatRenderer.Apply(referenceInput, map);
            var classified = RenderPipelineTestSupport.ReadPixels(referenceInput);
            if (isRaw) new AgxCrossing(raw, wb, map, locals: locals).Apply(image);
            else
            {
                var normalized = ChromaticAdaptation.NormalizeForRender(wb);
                var tone = standard with { Fold = normalized.Fold };
                ToneLutApplicator.ApplyLocals(image, normalized.Matrix, ToneLut.ComposeCached(tone), tone, locals, null);
            }
            var actual = RenderPipelineTestSupport.ReadPixels(image);
            for (var pixel = 0; pixel < source.Length / 3; pixel++)
            {
                var offset = pixel * 3;
                var input = new Rgb(classified[offset] / 65535d, classified[offset + 1] / 65535d, classified[offset + 2] / 65535d);
                var lab = LocalsRangeOracle.Classify(
                    wb[0, 0] * input.R + wb[0, 1] * input.G + wb[0, 2] * input.B,
                    wb[1, 0] * input.R + wb[1, 1] * input.G + wb[1, 2] * input.B,
                    wb[2, 0] * input.R + wb[2, 1] * input.G + wb[2, 2] * input.B);
                var terms = settings.Locals.Select(l =>
                {
                    var t = Math.Clamp(((pixel % 64 + .5) / 64 - l.Cu) / l.Feather + .5, 0, 1);
                    var range = l.Luminance!;
                    var weight = (1 - t * t * (3 - 2 * t)) *
                        new LocalsRangeOracle.LightWindow(range.Lower, range.Upper, range.Softness).Weight(lab.L);
                    if (l.Hue is { } h && !mono)
                        weight *= new LocalsRangeOracle.HueWindow(h.Center, h.Width, h.Softness).Weight(lab.Hue) * LocalsRangeOracle.Reliability(lab.C);
                    return Local.Create(l.Exposure, weight, -l.Temperature, l.Tint, l.Saturation, kg, tg);
                }).ToArray();
                if (terms.All(l => l.Weight == 0)) continue;
                var expected = Reference(input, raw, standard, wb, terms, isRaw);
                var error = new[] { Math.Abs(Code(expected.R) - actual[offset]),
                    Math.Abs(Code(expected.G) - actual[offset + 1]), Math.Abs(Code(expected.B) - actual[offset + 2]) }.Max();
                Assert.True(error <= 1, $"{isRaw}/{mode}/{color}/{pixel}: {error} codes");
                max = Math.Max(max, error);
            }
        }
        Assert.Equal(source, RenderPipelineTestSupport.ReadPixels(basis.Pixels));
        output.WriteLine($"L8 production {(isRaw ? "RAW" : "standard")} max={max} Q16 codes");
    }
}
