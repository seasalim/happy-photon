using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsContractPrototypeTests
{
    [Fact]
    public void Candidates_ReportFidelityAcrossBoundsAndPickedWhiteBalance()
    {
        double[] gains = [1.2, 1, 0.8];
        var white = WhiteBalanceModel.EstimateFromGains(gains);
        var map = HueSatMap();
        var inputs = Samples().ToArray();
        // The DCP contract ends at Q16. Extended synthetic values start downstream of that seam.
        var dcpInputs = inputs.Where(v => v.R <= 1 && v.G <= 1 && v.B <= 1)
            .Select(v => Dcp(v.Quantize(), map)).ToArray();
        output.WriteLine($"G2 corpus: {inputs.Length} synthetic + {dcpInputs.Length} post-DCP pixels, " +
            $"48 local recipes; Picked gains=(1.2,1,0.8), effective white={white.kelvin:F6} K/{white.tint:F6} tint; " +
            "AsShot white=6504 K/0 tint. Relative shifts inherit WhiteBalanceModel's 2000–12000 K/±100 tint limits.");
        output.WriteLine("ΔE00: PrecisionDeltaE.Ciede2000; sRGB transfer decoded Rec.2020→XYZ D65→CIELAB, " +
            "quantized final working-space codes (not mislabelled sRGB primaries). LUTs: 65536 doubles/channel, setup excluded.");
        foreach (var isRaw in new[] { true, false })
        foreach (var candidate in Enum.GetValues<Candidate>())
        {
            var errors = new Errors();
            foreach (var ev in new[] { -3.0, 0, 3 })
            foreach (var picked in new[] { false, true })
            {
                var wb = picked ? WhiteBalanceModel.CreateGainMatrix(gains) : ChromaticAdaptation.Identity();
                var raw = Raw(ev);
                var standard = Standard(ev);
                var kernel = new Kernel(candidate, raw, standard, wb, isRaw);
                var cases = LocalCases(picked ? white.kelvin : 6504, picked ? white.tint : 0);
                foreach (var locals in cases)
                {
                    var prepared = Prepare(locals);
                    foreach (var v in inputs.Concat(dcpInputs))
                    {
                        var expected = Reference(v, raw, standard, wb, locals, isRaw);
                        var actual = kernel.Render(v, prepared);
                        errors.Add(expected, actual);
                    }
                }
            }
            output.WriteLine($"G2 {(isRaw ? "RAW" : "standard")} {candidate}: {errors}.");
            if (candidate == Candidate.Analytic) Assert.InRange(errors.MaxCode, 0, 1);
        }
        output.WriteLine("Overflow: B allocates [0,16] scene-linear with exposure/fold composed into nodes, " +
            "clamps >16 to the upper tone-window result (may lose recoverable values under negative global EV); " +
            "C clamps log index to [0,1]. Standard C inherits AgX's nonzero floor and is informational, not recommended.");
    }

    [Fact]
    public void RangeBasis_UsesExtendedLinearRec2020OklabNotLuminance()
    {
        var v = new Rgb(0.6, 0.3, 0.1);
        var wb = WhiteBalanceModel.CreateGainMatrix([1.2, 1, 0.8]);
        var projected = Matrix(wb, v);
        var basis = OklabColor.FromLinearRec2020(new(projected.R, projected.G, projected.B));
        var scaled = OklabColor.FromLinearRec2020(new(8 * projected.R, 8 * projected.G, 8 * projected.B));
        Assert.Equal(2 * basis.Lightness, scaled.Lightness, 12);
        Assert.Equal(2 * basis.Chroma, scaled.Chroma, 12);
        Assert.Equal(basis.HueRadians, scaled.HueRadians, 12);
        var y = Matrix(RgbColorSpaceMatrices.LinearRec2020ToXyzD65DerivedExact, projected).G;
        Assert.NotEqual(y, basis.Lightness);
        Assert.True(scaled.Lightness > 1);
        output.WriteLine($"Range basis sample (0.6,0.3,0.1), Picked WB (1.2,1,0.8): " +
            $"OKLab L={basis.Lightness:F9}, C={basis.Chroma:F9}, hue={basis.HueRadians * 180 / Math.PI:F9}°, Y={y:F9}; " +
            $"8× input L={scaled.Lightness:F9}, C={scaled.Chroma:F9}. Conversion is unclamped; " +
            "UI mapping and pre-tone hue reliability remain Claude's normative spec decisions.");
    }

    [Fact]
    public void Analytic_ComposedCurvesAndNonNeutralToneMatchIndependentOracle()
    {
        var master = new CurveData();
        master.AddPointAndReturnIndex(0.4, 0.5);
        var red = new CurveData();
        red.AddPointAndReturnIndex(0.6, 0.45);
        var raw = Raw(-3) with { SourceExposureEv = 0.5, Contrast = 35, Highlights = -45,
            Shadows = 30, Curve = master, CurveRed = red };
        var standard = Standard(-3) with { Brightness = 10, Contrast = 35, Highlights = -45,
            Shadows = 30, BaseLookEnabled = true, Curve = master, CurveRed = red };
        var wb = WhiteBalanceModel.CreateMatrix(4800, 12, 6504, 0);
        Local[] locals = [Local.Create(ev: 4, weight: 0.5, mired: -50, tint: 50, saturation: 100)];
        foreach (var isRaw in new[] { true, false })
        {
            var kernel = new Kernel(Candidate.Analytic, raw, standard, wb, isRaw);
            var errors = new Errors();
            foreach (var v in Samples())
                errors.Add(Reference(v, raw, standard, wb, locals, isRaw), kernel.Render(v, Prepare(locals)));
            output.WriteLine($"Composed channel→master curves, non-neutral tone, {(isRaw ? "RAW" : "standard")}: {errors}.");
            Assert.InRange(errors.MaxCode, 0, 1);
        }
    }

    [Fact]
    public void EightOverlapExtremes_StayFiniteAndToneIsMonotoneThroughWindowEdges()
    {
        var maximum = 0.0;
        foreach (var sign in new[] { -1, 1 })
        foreach (var colorSign in new[] { -1, 1 })
        {
            var locals = Enumerable.Repeat(Local.Create(4 * sign, 1, 50 * colorSign,
                50 * colorSign, 100 * colorSign), 8).ToArray();
            var prepared = Prepare(locals);
            var previous = new Rgb(0, 0, 0);
            for (var i = 0; i <= 2048; i++)
            {
                var input = new Rgb(0.6, 0.3, 0.1) * (16 * i / 2048.0);
                var v = CandidateLocals(input, prepared, false);
                Assert.True(double.IsFinite(v.R) && double.IsFinite(v.G) && double.IsFinite(v.B));
                // Signed scene channels may decrease below zero; monotonicity is required
                // at the tone output, after the production post-inset input clamp.
                var inset = Matrix(AgxToneEngine.InsetMatrix, v);
                var oldInset = Matrix(AgxToneEngine.InsetMatrix, previous);
                Assert.True(ReferenceRaw(inset.R, Raw(3)) >= ReferenceRaw(oldInset.R, Raw(3)));
                Assert.True(ReferenceRaw(inset.G, Raw(3)) >= ReferenceRaw(oldInset.G, Raw(3)));
                Assert.True(ReferenceRaw(inset.B, Raw(3)) >= ReferenceRaw(oldInset.B, Raw(3)));
                maximum = Math.Max(maximum, Math.Max(v.R, Math.Max(v.G, v.B)));
                previous = v;
            }
        }
        output.WriteLine($"Eight-overlap ±32 EV and signed color extremes: finite, monotone pre-outset tone; " +
            $"max scene channel={maximum:G12}; window=[{0.18 * Math.Pow(2, -10):G12}, {0.18 * Math.Pow(2, 6.5):G12}].");
    }

    [Fact]
    public void Candidates_ReportMedianFivePixelCostAt1600Scale()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to measure locals candidate kernel costs.");
        const int count = 1600 * 1067;
        var samples = Samples().Where(v => v.R <= 1 && v.G <= 1 && v.B <= 1).ToArray();
        var wb = WhiteBalanceModel.CreateGainMatrix([1.2, 1, 0.8]);
        var white = WhiteBalanceModel.EstimateFromGains([1.2, 1, 0.8]);
        var one = Prepare([Local.Create(1, 0.5, 20, -15, 35, white.kelvin, white.tint)]);
        var eight = Enumerable.Repeat(one[0], 8).ToArray();
        output.WriteLine($"G3 scalar single-thread {count:N0} pixels; warm median of 5; " +
            $"{Environment.Version}, {Environment.ProcessorCount} logical CPUs. Setup, allocation, DCP and masks excluded.");
        foreach (var isRaw in new[] { true, false })
        {
            var legacy = new Kernel(Candidate.Analytic, Raw(-1), Standard(-1), wb, isRaw);
            output.WriteLine($"G3 {(isRaw ? "RAW" : "standard")} today's LUT kernel: " +
                $"{Measure(i => legacy.Legacy(samples[i % samples.Length]), count):F2} ns/pixel.");
            foreach (var candidate in Enum.GetValues<Candidate>())
            {
                var kernel = new Kernel(candidate, Raw(-1), Standard(-1), wb, isRaw);
                output.WriteLine($"G3 {(isRaw ? "RAW" : "standard")} {candidate}: " +
                    $"one={Measure(i => kernel.Render(samples[i % samples.Length], one), count):F2}, " +
                    $"eight={Measure(i => kernel.Render(samples[i % samples.Length], eight), count):F2} ns/pixel.");
            }
        }
        var map = HueSatMap();
        var source = new ushort[count * 3];
        for (var i = 0; i < count; i++)
        {
            var v = samples[i % samples.Length];
            source[3 * i] = (ushort)Code(v.R);
            source[3 * i + 1] = (ushort)Code(v.G);
            source[3 * i + 2] = (ushort)Code(v.B);
        }
        var scratch = new ushort[source.Length];
        var times = new double[5];
        DcpHueSatRenderer.ApplyValues(scratch, count, 3, 0, 1, 2, map);
        for (var repeat = 0; repeat < times.Length; repeat++)
        {
            source.CopyTo(scratch, 0);
            var clock = Stopwatch.StartNew();
            DcpHueSatRenderer.ApplyValues(scratch, count, 3, 0, 1, 2, map);
            times[repeat] = clock.Elapsed.TotalNanoseconds / count;
        }
        Array.Sort(times);
        output.WriteLine($"G3 DCP lattice production parallel pass: {times[2]:F2} ns/pixel " +
            "(copy/setup excluded; not directly comparable to single-thread kernels).");
        Assert.NotEqual(source, scratch);
    }

    private static double Measure(Func<int, Rgb> pixel, int count)
    {
        var checksum = 0.0;
        for (var i = 0; i < 10000; i++) checksum += pixel(i).R;
        var times = new double[5];
        for (var repeat = 0; repeat < times.Length; repeat++)
        {
            var clock = Stopwatch.StartNew();
            for (var i = 0; i < count; i++)
            {
                var v = pixel(i);
                checksum += Code(v.R) + Code(v.G) + Code(v.B);
            }
            times[repeat] = clock.Elapsed.TotalNanoseconds / count;
        }
        Assert.True(double.IsFinite(checksum) && checksum > 0);
        Array.Sort(times);
        return times[2];
    }

    private static IEnumerable<Local[]> LocalCases(double kelvin, double tint)
    {
        foreach (var weight in new[] { 0.0, 0.5, 1 })
        {
            foreach (var ev in new[] { -4.0, -1, 0, 1, 4 })
                yield return [Local.Create(ev, weight)];
            foreach (var sign in new[] { -1, 1 })
            {
                yield return [Local.Create(mired: 50 * sign, weight: weight, kelvin: kelvin, globalTint: tint)];
                yield return [Local.Create(tint: 50 * sign, weight: weight, kelvin: kelvin, globalTint: tint)];
                yield return [Local.Create(saturation: 100 * sign, weight: weight)];
                var combined = Local.Create(4 * sign, weight, 50 * sign, 50 * sign, 100 * sign, kelvin, tint);
                yield return [combined];
                yield return Enumerable.Repeat(combined, 8).ToArray();
            }
            yield return [Local.Create(mired: 50, weight: weight, kelvin: kelvin, globalTint: tint),
                Local.Create(saturation: -80, weight: weight)];
        }
    }

    private static IEnumerable<Rgb> Samples()
    {
        for (var i = 0; i <= 256; i++)
        {
            var v = i / 256.0;
            yield return new(v, v, v);
            yield return new(16 * v, 16 * v, 16 * v);
            yield return new(v, v * 0.5, v * 0.1);
        }
        foreach (var v in new[] { 0, 1e-12, 1e-8, 1 / 65535.0, 0.18 / 1024, 0.0031308,
            0.18, 0.75, 1, 2, 16, 0.18 * Math.Pow(2, 6.5), 17 })
        {
            yield return new(v, 0, 0);
            yield return new(0, v, 0);
            yield return new(0, 0, v);
            yield return new(v, v, v);
        }
        var random = new Random(239);
        for (var i = 0; i < 256; i++)
        {
            var v = new Rgb(random.NextDouble(), random.NextDouble(), random.NextDouble());
            yield return v;
            yield return v * 16;
        }
    }

    private sealed class Errors
    {
        internal int MaxCode { get; private set; }
        private double _sum;
        private double _max;
        private int _count;

        internal void Add(Rgb expected, Rgb actual)
        {
            Assert.True(double.IsFinite(actual.R) && double.IsFinite(actual.G) && double.IsFinite(actual.B));
            MaxCode = Math.Max(MaxCode, Error(expected, actual));
            var delta = PrecisionDeltaE.Ciede2000(Lab(expected.Quantize()), Lab(actual.Quantize()));
            _sum += delta;
            _max = Math.Max(_max, delta);
            _count++;
        }

        public override string ToString() => $"n={_count}, max code={MaxCode}, mean ΔE00={_sum / _count:F9}, max ΔE00={_max:F9}";

        private static PrecisionLab Lab(Rgb v)
        {
            var xyz = Matrix(RgbColorSpaceMatrices.LinearRec2020ToXyzD65DerivedExact,
                new(Decode(v.R), Decode(v.G), Decode(v.B)));
            static double F(double t) => t > 216.0 / 24389 ? Math.Cbrt(t) : (24389.0 / 27 * t + 16) / 116;
            var x = F(xyz.R / 0.95047);
            var y = F(xyz.G);
            var z = F(xyz.B / 1.08883);
            return new(116 * y - 16, 500 * (x - y), 200 * (y - z));
        }
    }
}
