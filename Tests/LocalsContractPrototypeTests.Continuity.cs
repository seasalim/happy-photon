using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsContractPrototypeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Analytic_IsContinuousAtZeroUnderExtremeCustomWhiteBalance(bool isRaw)
    {
        Rgb[] primaries = [new(0, 1, 0), new(1, 0, 0), new(0, 0, 1)];
        foreach (var kelvin in new[] { 2000.0, 12000 })
        {
            var wb = CustomWhite(kelvin, 0);
            var kernel = new Kernel(Candidate.Analytic, Raw(), Standard(), wb, isRaw);
            var maximum = 0;
            foreach (var input in primaries)
            {
                var expected = ProductionTone(input, wb, isRaw);
                Assert.Equal(expected, kernel.Render(input, []).Quantize());
                foreach (var sign in new[] { -1, 1 })
                {
                    Local[] cases =
                    [
                        Local.Create(ev: sign * 4, weight: 1e-12, mired: sign * 50,
                            tint: sign * 50, saturation: sign * 100, kelvin: kelvin),
                        Local.Create(ev: sign * 1e-9, kelvin: kelvin),
                        Local.Create(mired: sign * 1e-9, kelvin: kelvin),
                        Local.Create(tint: sign * 1e-9, kelvin: kelvin),
                        Local.Create(saturation: sign * 1e-9, kelvin: kelvin)
                    ];
                    foreach (var local in cases)
                    {
                        var actual = kernel.Render(input, Prepare([local]));
                        var reference = Reference(input, Raw(), Standard(), wb, [local], isRaw);
                        var error = Math.Max(Error(expected, actual), Error(expected, reference));
                        maximum = Math.Max(maximum, error);
                        Assert.True(error <= 1, $"RAW={isRaw}, Kelvin={kelvin}, {input}, {local}: {error} codes");
                    }
                }
            }
            output.WriteLine($"R2-1 {(isRaw ? "RAW" : "standard")}, Custom {kelvin} K, " +
                $"three primaries, signed tiny weight/EV/mired/tint/saturation: max {maximum} codes vs production.");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LocalNegativeMired_WarmsGrayAndMatchesGlobalControl(bool isRaw)
    {
        // Independently specify a Kelvin step, then derive its local mired equivalent.
        // The production comparator goes through Custom settings and the real tone pass.
        const double effectiveKelvin = 6504;
        const double shiftedKelvin = 8000;
        var input = new Rgb(0.18, 0.18, 0.18).Quantize();
        var wb = CustomWhite(effectiveKelvin, 0);
        var kernel = new Kernel(Candidate.Analytic, Raw(), Standard(), wb, isRaw);
        var identity = ProductionTone(input, wb, isRaw);
        foreach (var tint in new[] { 0.0, 15 })
        {
            Local[] locals = [Local.Create(mired: 1e6 / shiftedKelvin - 1e6 / effectiveKelvin,
                tint: tint, kelvin: effectiveKelvin)];
            var actual = kernel.Render(input, Prepare(locals)).Quantize();
            var expected = ProductionTone(input, CustomWhite(shiftedKelvin, tint), isRaw);
            Assert.True(actual.R / actual.B > identity.R / identity.B);
            Assert.InRange(Error(expected, actual), 0, 1);
            Assert.InRange(Error(expected, Reference(input, Raw(), Standard(), wb, locals, isRaw)), 0, 1);
            output.WriteLine($"R2-2 {(isRaw ? "RAW" : "standard")}, 6504→8000 K, tint {tint}: " +
                $"R/B {identity.R / identity.B:F9}→{actual.R / actual.B:F9}, global error {Error(expected, actual)} codes.");
        }
    }

    private static double[,] CustomWhite(double kelvin, double tint)
    {
        using var image = RenderPipelineTestSupport.CreateBase([0, 0, 0]);
        return RenderChromaticStage.CreateWhiteBalanceMatrix(image.Info,
            new EditSettings { Wb = new() { Mode = WbMode.Custom, Kelvin = kelvin, Tint = tint } });
    }

    private static Rgb ProductionTone(Rgb input, double[,] wb, bool isRaw)
    {
        using var image = RenderPipelineTestSupport.CreateBase(Flatten([input]));
        if (isRaw) new AgxCrossing(Raw(), wb).Apply(image.Pixels);
        else
        {
            var normalized = ChromaticAdaptation.NormalizeForRender(wb);
            ToneLutApplicator.Apply(image.Pixels, normalized.Matrix,
                ToneLut.Compose(Standard() with { Fold = normalized.Fold }));
        }
        var codes = RenderPipelineTestSupport.ReadPixels(image.Pixels);
        return new(codes[0] / 65535.0, codes[1] / 65535.0, codes[2] / 65535.0);
    }
}
