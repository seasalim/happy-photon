using Xunit;
using static HappyPhoton.Tests.LocalsRangeOracle;

namespace HappyPhoton.Tests;

public sealed partial class LocalsContractPrototypeTests
{
    [Fact]
    public void RangeFast_GuardedFidelityAtBoundariesAndExtendedRgb()
    {
        var fromLab = PrecisionColorCases.Invert(new[,]
        {
            { .2104542553, .7936177850, -.0040720468 },
            { 1.9779984951, -2.4285922050, .4505937099 },
            { .0259040371, .7827717662, -.8086757660 }
        });
        var fromLms = PrecisionColorCases.Invert(CreateMatrix());
        double[] Rgb(Lab lab)
        {
            var roots = PrecisionColorCases.Transform(fromLab, [lab.L, lab.A, lab.B]);
            return PrecisionColorCases.Transform(fromLms, roots.Select(x => x * x * x).ToArray());
        }
        var random = new Random(261);
        var rgb = new List<double[]> { new double[3], new[] { -2d, .1, 3 }, new[] { 8d, 8, 8 } };
        for (var i = 0; i < 10000; i++)
            rgb.Add(Enumerable.Range(0, 3).Select(_ => random.NextDouble() * 12 - 4).ToArray());
        for (var i = 0; i < 100000; i++)
            rgb.Add(Enumerable.Range(0, 3).Select(_ => random.NextDouble()).ToArray());
        // Exercise every LUT cell midpoint, both signs, including the fallback seams.
        for (var i = 0; i < 262144; i++)
        {
            var v = (i + .5) * 4 / 262144;
            rgb.Add(PrecisionColorCases.Transform(fromLms, [v, v, v]));
            rgb.Add(PrecisionColorCases.Transform(fromLms, [-v, -v, -v]));
        }
        double maxL = 0, maxSoft = 0;
        int hardFlips = 0, count = 0, fallbacks = 0, approximate = 0;
        foreach (var softness in new[] { 0d, 1e-6, 1e-3, .1 })
        {
            (LightWindow L, HueWindow H)[] windows =
            [ (new(.25, .75, softness), new(0, 60, softness)),
              (new(0, 0, softness), new(240, 360, softness)),
              (new(1, 1, softness), new(180, 0, softness)),
              (new(.5, .5, softness), new(359, 60, softness)) ];
            var fast = new LocalsRangeFast(windows);
            var adjacent = new List<double[]>();
            foreach (var (l, h) in windows)
            {
                foreach (var endpoint in new[] { l.Lower - softness, l.Lower, l.Upper, l.Upper + softness })
                foreach (var value in Adjacent(endpoint))
                    adjacent.Add(Rgb(new(value, .1, 0)));
                foreach (var endpoint in new[] { 0d, 360, h.Center - h.Width / 2 - softness,
                             h.Center - h.Width / 2, h.Center + h.Width / 2, h.Center + h.Width / 2 + softness })
                foreach (var value in Adjacent(endpoint))
                foreach (var c in new[] { .01, .025, .04, .1 })
                    adjacent.Add(Rgb(new(.5, c * Math.Cos(value * Math.PI / 180), c * Math.Sin(value * Math.PI / 180))));
            }
            foreach (var v in rgb.Concat(adjacent))
            {
                var oracle = Classify(v[0], v[1], v[2]);
                var raw = LocalsRangeFast.Approximate(v[0], v[1], v[2], out _);
                maxL = Math.Max(maxL, Math.Abs(raw.L - oracle.L));
                Assert.InRange(Math.Abs(raw.L - oracle.L), 0, LocalsRangeFast.LightError);
                Assert.InRange(Math.Sqrt(Math.Pow(raw.A - oracle.A, 2) + Math.Pow(raw.B - oracle.B, 2)),
                    0, LocalsRangeFast.ChromaError);
                var guarded = fast.ClassifyGuarded(v[0], v[1], v[2], 255, out var fallback);
                if (fallback) fallbacks++; else approximate++;
                foreach (var (l, h) in windows)
                {
                    var expectedL = l.Weight(oracle.L); var expectedH = h.Weight(oracle.Hue);
                    var actualL = l.Weight(guarded.L); var actualH = h.Weight(guarded.Hue);
                    if (softness == 0 && (expectedL != actualL || expectedH != actualH)) hardFlips++;
                    maxSoft = Math.Max(maxSoft, Math.Abs(expectedL * expectedH * Reliability(oracle.C) -
                        actualL * actualH * Reliability(guarded.C)));
                }
                count++;
            }
        }
        Assert.Equal(0, hardFlips); Assert.InRange(maxSoft, 0, 1e-3);
        Assert.True(approximate > 0); Assert.True(fallbacks > 0);
        output.WriteLine($"FAST samples={count} hard_flips={hardFlips} max_soft_error={maxSoft:R} " +
            $"max_unprotected_dL={maxL:R} approximate={approximate} oracle_fallbacks={fallbacks}");
    }
}
