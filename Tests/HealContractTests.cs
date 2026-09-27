using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class HealContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ZeroFeatherFlatFieldMatchesDestinationExactly(bool floor)
    {
        const int width = 100, height = 60;
        // C-2: 0.6 + 0.35 = 0.95, independently quantized to the destination code.
        var input = Enumerable.Range(0, width * height * 3)
            .Select(i => (ushort)(i / 3 % width < 50 ? 39321 : 62258)).ToArray();
        if (floor) input = input.Select(v => (ushort)(65535 - v)).ToArray();
        var spot = new HealSpot(.75, .5, .25, .5, .1, Feather: 0);
        using var basis = RenderPipelineTestSupport.CreateBase(input, height: height);
        foreach (var candidate in HealCandidate.FinalOnly)
        {
            using var repair = new HealProductionStage().Repair(basis, [spot], candidate);
            Assert.Equal(input, RenderPipelineTestSupport.ReadPixels(repair.Pixels));
            Assert.Equal(input, HealOracle.Apply(input, width, height, [spot], candidate));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NearLimitRampRetainsDetailAfterQ16AndMinusTwoEv(bool floor)
    {
        const int width = 800, height = 200;
        var input = Enumerable.Range(0, width * height * 3)
            .Select(i => (ushort)(i / 3 % width < 400 ? 13107 : 45875)).ToArray();
        // C-1: all 64 source codes 65400..65463 sit inside a constant boundary.
        // Mirror the entire scene for the additive floor regression.
        for (var i = 0; i < 64; i++)
        for (var c = 0; c < 3; c++)
            input[(100 * width + 168 + i) * 3 + c] = (ushort)(65400 + i);
        if (floor) input = input.Select(v => (ushort)(65535 - v)).ToArray();
        var spot = new HealSpot(600.5 / width, 100.5 / height, 200.5 / width, 100.5 / height,
            HealWorkloads.MaxRadius, Feather: 0);
        using var basis = RenderPipelineTestSupport.CreateBase(input, height: height);
        foreach (var candidate in HealCandidate.FinalOnly.Where(c => !floor || c.Domain == HealDomain.Additive))
        {
            using var repair = new HealProductionStage().Repair(basis, [spot], candidate);
            var codes = RenderPipelineTestSupport.ReadPixels(repair.Pixels);
            var oracle = HealOracle.Apply(input, width, height, [spot], candidate);
            // Independent Q16 expectations from the published rule (TESTING.md), without
            // calling either kernel: constant boundary correction 32768/65535, headroom less
            // the 0.51-code reserve, 7/8 knee, shoulder approaching 29/32 of that headroom.
            var expected = Enumerable.Range(0, 64).Select(i =>
            {
                double source = (65400 + i) / 65535d, m = 32768 / 65535d;
                var room = 1 - source - .51 / 65535;
                double knee = 7 * room / 8, shoulder = room / 32;
                var adjusted = m <= knee ? m : knee + shoulder - shoulder * shoulder / (m - knee + shoulder);
                return (ushort)Math.Round(65535 * (source + adjusted));
            }).ToArray();
            if (floor) expected = expected.Select(v => (ushort)(65535 - v)).ToArray();
            var ramp = Enumerable.Range(0, 64).Select(i => codes[(100 * width + 568 + i) * 3]).ToArray();
            Assert.Equal(expected, ramp);
            Assert.Equal(floor ? (ushort)13 : (ushort)65522, ramp[0]);
            Assert.Equal(floor ? (ushort)7 : (ushort)65528, ramp[^1]);
            Assert.Equal(7, ramp.Distinct().Count());
            Assert.True(ramp.Select(v => Math.Round(v / 4d)).Distinct().Count() > 1);
            var disc = Enumerable.Range(0, width * height).Where(p =>
                Math.Pow(p % width - 600, 2) + Math.Pow(p / width - 100, 2) < 80 * 80).ToArray();
            Assert.All(disc, p => Assert.InRange(codes[p * 3], (ushort)1, (ushort)65534));
            Assert.All(Enumerable.Range(0, codes.Length), i =>
                Assert.InRange(Math.Abs(codes[i] - oracle[i]), 0, 1));
            // Also exercise the actual Export-intent exposure path, not just code / 4.
            using var exposed = new RenderPipeline().Render(new(repair, new EditSettings { Exposure = -2 },
                RenderIntent.Export, null, new(false, false)));
            var rendered = RenderPipelineTestSupport.ReadPixels(exposed.Image);
            Assert.True(Enumerable.Range(0, 64).Select(i => rendered[(100 * width + 568 + i) * 3])
                .Distinct().Count() > 1);
        }
    }

    [Fact]
    public void AttenuationPreservesIdentityContinuityAndSourceSlopeAtBothLimits()
    {
        // In-range corrections below the knee are exact; 0.6 + 0.35 sits 0.45 code past it
        // (the reserve) and still lands within a thousandth of a code.
        Assert.Equal(.9499, .6 + RenderRepairs.Attenuate(.6, .3499), 14);
        Assert.InRange(Math.Abs(.6 + RenderRepairs.Attenuate(.6, .35) - .95) * 65535, 0, .001);
        foreach (var direction in new[] { -1, 1 })
        {
            const double source = .5, epsilon = 1e-7;
            var knee = 7d / 8 * (.5 - RenderRepairs.QuantumReserve);
            double At(double magnitude) => source + RenderRepairs.Attenuate(source, direction * magnitude);
            Assert.Equal(source + direction * knee, At(knee), 14);
            Assert.InRange(direction * (At(knee) - At(knee - epsilon)) / epsilon, .9999, 1.0001);
            Assert.InRange(direction * (At(knee + epsilon) - At(knee)) / epsilon, .9999, 1.0001);
            foreach (var magnitude in new[] { .0001, .01, .35, .5, 2, 1000 })
            {
                double previous = RenderRepairs.Attenuate(0, direction * magnitude);
                for (var i = 1; i <= 4096; i++)
                {
                    var s = i / 4096d;
                    var delta = RenderRepairs.Attenuate(s, direction * magnitude);
                    var value = s + delta;
                    Assert.InRange(value, 0, 1);
                    Assert.InRange(direction * delta, 0, magnitude);
                    Assert.InRange((value - previous) * 4096, 3d / 32 - 1e-9, 1 + 1e-9);
                    previous = value;
                }
            }
        }
    }

    [Theory]
    [InlineData(200, 20)]
    [InlineData(20, 200)]
    [InlineData(400, 40)]
    [InlineData(40, 400)]
    public void PanoramicMaximumRadiusClampsSourceAndAllowsDestinationEdgeCrossing(int width, int height)
    {
        var input = Enumerable.Range(0, width * height * 3)
            .Select(i => (ushort)(100 * (i / 3 % width) + 100 * (i / 3 / width))).ToArray();
        using var basis = RenderPipelineTestSupport.CreateBase(input, height: height);
        var radius = Math.Min(width, height) / 2d; // Independent expected radius at 10:1.
        foreach (var edge in new[] { 0d, 1d })
        {
            var spot = new HealSpot(width > height ? .5 : edge, width > height ? edge : .5,
                edge, edge, HealWorkloads.MaxRadius, IsClone: true, Feather: 0);
            var cx = spot.U * width - .5; var cy = spot.V * height - .5;
            // A maximum source disc touches both short edges, regardless of Su/Sv.
            var sx = (edge == 0 ? radius : width - radius) - .5;
            var sy = (edge == 0 ? radius : height - radius) - .5;
            var expected = (ushort[])input.Clone();
            var changed = 0;
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) >= radius * radius) continue;
                var sampleX = Math.Clamp(sx + x - cx, 0, width - 1);
                var sampleY = Math.Clamp(sy + y - cy, 0, height - 1);
                var code = (ushort)Math.Round(100 * sampleX + 100 * sampleY);
                for (var c = 0; c < 3; c++) expected[(y * width + x) * 3 + c] = code;
                changed++;
            }
            Assert.True(changed > 0);
            using var repair = new HealProductionStage().Repair(basis, [spot], default);
            Assert.Equal(expected, RenderPipelineTestSupport.ReadPixels(repair.Pixels));
            Assert.Equal(expected, HealOracle.Apply(input, width, height, [spot], default));

            // The same clamped footprint and source boundary must agree for both heals/domains.
            foreach (var candidate in HealCandidate.FinalOnly)
            {
                var heal = spot with { IsClone = false };
                using var healed = new HealProductionStage().Repair(basis, [heal], candidate);
                var codes = RenderPipelineTestSupport.ReadPixels(healed.Pixels);
                var oracle = HealOracle.Apply(input, width, height, [heal], candidate);
                Assert.All(Enumerable.Range(0, codes.Length), i =>
                    Assert.InRange(Math.Abs(codes[i] - oracle[i]), 0, 1));
            }
        }
        Assert.Equal(input, RenderPipelineTestSupport.ReadPixels(basis.Pixels));
    }

    [Fact]
    public void AffineDestinationIsReproducedWithoutSeamFromOutwardSamples()
    {
        // Constant source, affine destination: the healed disc must equal the destination ramp.
        const int width = 480, height = 320;
        static double Ramp(int x) => 30000 + 100 * (x - 360);
        var input = new ushort[width * height * 3];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        for (var c = 0; c < 3; c++)
            input[(y * width + x) * 3 + c] = (ushort)(x < 240 ? 20000 : Ramp(x));
        var spot = new HealSpot(360.5 / width, 160.5 / height, 100.5 / width, 160.5 / height, 40d / width, Feather: 0);
        using var basis = RenderPipelineTestSupport.CreateBase(input, height: height);
        var candidate = new HealCandidate(HealFormulation.Membrane, HealDomain.Additive);
        using var repair = new HealProductionStage().Repair(basis, [spot], candidate);
        var codes = RenderPipelineTestSupport.ReadPixels(repair.Pixels);
        var oracle = HealOracle.Apply(input, width, height, [spot], candidate);
        double worst = 0;
        for (var y = 120; y <= 200; y++)
        for (var x = 320; x <= 400; x++)
        {
            if ((x - 360) * (x - 360) + (y - 160) * (y - 160) >= 40 * 40) continue;
            worst = Math.Max(worst, Math.Abs(codes[(y * width + x) * 3] - Ramp(x)));
            Assert.InRange(Math.Abs(codes[(y * width + x) * 3] - oracle[(y * width + x) * 3]), 0, 1);
        }
        // 32 discrete Poisson samples reproduce an affine field up to their angular spacing;
        // the ramp steps 100 codes per pixel, so the bound is under half a pixel of ramp.
        Assert.InRange(worst, 0, 40);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AttenuationNeverRoundsOntoARangeLimitTheSourceDidNotReach(bool floor)
    {
        var previous = -1;
        for (var code = 65520; code <= 65534; code++)
        {
            var source = (floor ? 65535 - code : code) / 65535d;
            var correction = floor ? -.5 : .5;
            var result = (int)Math.Round((source + RenderRepairs.Attenuate(source, correction)) * 65535);
            var mirrored = floor ? 65535 - result : result;
            Assert.InRange(mirrored, code, 65534);
            Assert.True(mirrored >= previous, $"code {code} decreased to {mirrored}");
            previous = mirrored;
        }
        var limit = floor ? 0d : 1d;
        Assert.Equal(0, RenderRepairs.Attenuate(limit, floor ? -.5 : .5));
    }
}
