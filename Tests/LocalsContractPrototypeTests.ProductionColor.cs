using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsContractPrototypeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProductionColorMatchesIndependentOracle(bool isRaw)
    {
        var random = new Random(246);
        var source = Enumerable.Range(0, 64 * 32 * 3).Select(_ => (ushort)random.Next(65536)).ToArray();
        using var basis = RenderPipelineTestSupport.CreateBase(source, isRaw, 32);
        var max = 0;
        foreach (var mode in Enum.GetValues<WbMode>())
        // The color arm's chromatic pixels with Saturation +100 / Tint -50 also
        // exercise negative post-composition channels before standard tone.
        foreach (var arm in new[] { "color", "combined", "eight", "reverse", "half", "near-zero" })
        {
            var settings = new EditSettings { Wb = new() { Mode = mode, Kelvin = 8500, Tint = 30,
                Gains = [1.8, 1, .6] } };
            var (kg, tg) = mode switch
            {
                WbMode.Custom or WbMode.Preset => (8500d, 30d),
                WbMode.Picked => WhiteBalanceModel.EstimateFromGains(settings.Wb.Gains),
                _ => (basis.Info.AsShotKelvin, basis.Info.AsShotTint)
            };
            settings.Locals = Enumerable.Range(0, arm is "eight" or "reverse" ? 8 : 1).Select(i =>
                new LocalAdjustment { Ordinal = i + 1, Angle = 0, Cu = arm == "half" ? .5 : 2,
                    Feather = arm == "half" ? 2 : .25, Temperature = i % 2 == 0 ? 50 : -40,
                    Tint = i % 2 == 0 ? -50 : 30, Saturation = i % 2 == 0 ? 100 : -70,
                    Exposure = arm == "combined" ? 2 : 0 }).ToList();
            if (arm == "reverse") settings.Locals.Reverse();
            if (arm == "near-zero") { settings.Locals[0].Cu = -.12; settings.Locals[0].Feather = .25; }
            var raw = Raw(-2) with { Contrast = 25, Highlights = -40, Shadows = 15 };
            var standard = Standard(-2) with { Brightness = 20, BaseLookEnabled = true, Highlights = -40 };
            var wb = RenderChromaticStage.CreateWhiteBalanceMatrix(basis.Info, settings);
            using var image = RenderGeometry.Apply(basis.Pixels, settings, out var trace);
            var locals = RenderLocals.Create(settings, trace, 64, 32, info: basis.Info)!;
            if (isRaw) new AgxCrossing(raw, wb, locals: locals).Apply(image);
            else
            {
                var normalized = ChromaticAdaptation.NormalizeForRender(wb);
                var tone = standard with { Fold = normalized.Fold };
                ToneLutApplicator.ApplyLocals(image, normalized.Matrix, ToneLut.ComposeCached(tone), tone, locals, null);
            }
            var actual = RenderPipelineTestSupport.ReadPixels(image);
            for (var pixel = 0; pixel < source.Length / 3; pixel++)
            {
                var terms = settings.Locals.Select(l =>
                {
                    var t = Math.Clamp(((pixel % 64 + .5) / 64 - l.Cu) / l.Feather + .5, 0, 1);
                    return Local.Create(l.Exposure, 1 - t * t * (3 - 2 * t), -l.Temperature,
                        l.Tint, l.Saturation, kg, tg);
                }).ToArray();
                // Exactly zero weights intentionally use the shipped LUT path, tested by parity sentinels.
                if (terms.All(l => l.Weight == 0)) continue;
                var offset = pixel * 3;
                var input = new Rgb(source[offset] / 65535d, source[offset + 1] / 65535d, source[offset + 2] / 65535d);
                var expected = Reference(input, raw, standard, wb, terms, isRaw);
                var error = new[] { Math.Abs(Code(expected.R) - actual[offset]),
                    Math.Abs(Code(expected.G) - actual[offset + 1]), Math.Abs(Code(expected.B) - actual[offset + 2]) }.Max();
                Assert.True(error <= 1, $"{isRaw}/{mode}/{arm}/{pixel}: {error} codes");
                max = Math.Max(max, error);
            }
        }
        Assert.Equal(source, RenderPipelineTestSupport.ReadPixels(basis.Pixels));
        output.WriteLine($"G4 production {(isRaw ? "RAW" : "standard")} max={max} Q16 codes");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProductionColorOrderHalfWeightAndContinuity(bool raw)
    {
        using var basis = RenderPipelineTestSupport.CreateBase([12000, 26000, 8000], raw);
        using var image = RenderGeometry.Apply(basis.Pixels, new(), out var trace);
        var first = new LocalAdjustment { Temperature = 50, Tint = -50, Cu = .5, Angle = 0 };
        var second = new LocalAdjustment { Saturation = -80, Cu = .5, Angle = 0 };
        var settings = new EditSettings { Locals = [first, second] };
        Rgb Apply()
        {
            var plan = RenderLocals.Create(settings, trace, 1, 1, info: basis.Info)!;
            double r = .2, g = .4, b = .1;
            Assert.True(plan.ApplyColor(0, ref r, ref g, ref b));
            return new(r, g, b);
        }
        var forward = Apply();
        var expected = ReferenceLocals(new(.2, .4, .1),
            [Local.Create(weight: .5, mired: -50, tint: -50), Local.Create(weight: .5, saturation: -80)], false);
        Assert.InRange(Math.Abs(forward.R - expected.R) + Math.Abs(forward.G - expected.G) + Math.Abs(forward.B - expected.B), 0, 1e-14);
        settings.Locals.Reverse();
        Assert.NotEqual(forward, Apply());
        settings.Locals = [first];
        foreach (var epsilon in new[] { 1e-3, 1e-5, 1e-7 })
        {
            first.Cu = .5 - first.Feather * (.5 - epsilon);
            var v = Apply();
            Assert.InRange(Math.Abs(v.R - .2) + Math.Abs(v.G - .4) + Math.Abs(v.B - .1), 0, 10 * epsilon * epsilon);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionColorNeutralAndMonochromeAreExact(bool mono)
    {
        ushort[] source = [0, 0, 0, 12000, 12000, 12000, 60000, 60000, 60000];
        using var basis = RenderPipelineTestSupport.CreateBase(source, true, isMonochrome: mono);
        var settings = new EditSettings { Locals = [new() { Exposure = 1, Cu = 2, Angle = 0 }] };
        using var image = RenderGeometry.Apply(basis.Pixels, settings, out var trace);
        ushort[] Render()
        {
            var values = (ushort[])source.Clone();
            var locals = RenderLocals.Create(settings, trace, 3, 1, info: basis.Info);
            new AgxCrossing(Raw(), locals: locals).Apply(values);
            return values;
        }
        var expected = Render();
        if (mono) { settings.Locals[0].Temperature = 50; settings.Locals[0].Tint = -50; settings.Locals[0].Saturation = 100; }
        Assert.Equal(expected, Render());
        if (mono) for (var i = 0; i < expected.Length; i += 3)
        { Assert.Equal(expected[i], expected[i + 1]); Assert.Equal(expected[i], expected[i + 2]); }
        settings.Locals[0].Exposure = 0;
        Assert.Null(RenderLocals.Create(settings, trace, 3, 1, info: basis.Info));
    }
}
