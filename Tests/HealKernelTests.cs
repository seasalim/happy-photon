using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class HealKernelTests
{
    [Fact]
    public void AllCandidatesMatchIndependentOracleAndWorkerCounts()
    {
        const int width = 97, height = 73;
        var random = new Random(277);
        var input = Enumerable.Range(0, width * height * 3).Select(_ => (ushort)random.Next(65536)).ToArray();
        var spots = new[]
        {
            new HealSpot(.43, .48, .47, .50, .10, Feather: .3),
            new HealSpot(.48, .49, .40, .47, .08, IsClone: true, Feather: 0, Opacity: .6),
            new HealSpot(.01, .98, .6, .6, .07, Opacity: .05),
            new HealSpot(.6, .6, .43, .48, .09)
        };
        using var basis = RenderPipelineTestSupport.CreateBase(input, height: height);
        foreach (var candidate in HealCandidate.All)
        {
            var expected = HealOracle.Apply(input, width, height, spots, candidate);
            ushort[]? first = null;
            foreach (var workers in new[] { 1, 2, 8, 2 })
            {
                using var actual = new HealPrototype().Repair(basis, spots, candidate, workers);
                var codes = Read(actual);
                var worst = Enumerable.Range(0, codes.Length).MaxBy(i => Math.Abs(codes[i] - expected[i]));
                Assert.True(Math.Abs(codes[worst] - expected[worst]) <= 1,
                    $"{candidate} at {worst}: input={input[worst]} basis={Read(basis)[worst]} actual={codes[worst]} expected={expected[worst]}");
                if (first != null) Assert.Equal(first, codes);
                first = codes;
            }
        }
        Assert.Equal(input, Read(basis));
    }

    [Fact]
    public void CloneSamplesSnapshotAndCreationOrder()
    {
        var input = Enumerable.Range(0, 40 * 30 * 3).Select(i => (ushort)(i * 13)).ToArray();
        using var basis = RenderPipelineTestSupport.CreateBase(input, height: 30);
        // Pixel (20,15) samples (21,15); overlap must not propagate newly written pixels.
        var a = new HealSpot(20.5 / 40, 15.5 / 30, 21.5 / 40, 15.5 / 30, .1, true, 0);
        var b = new HealSpot(10.5 / 40, 15.5 / 30, 20.5 / 40, 15.5 / 30, .1, true, 0);
        using var repaired = new HealPrototype().Repair(basis, [a, b], default);
        var codes = Read(repaired);
        for (var c = 0; c < 3; c++)
        {
            Assert.Equal(input[(15 * 40 + 21) * 3 + c], codes[(15 * 40 + 20) * 3 + c]);
            Assert.Equal(input[(15 * 40 + 21) * 3 + c], codes[(15 * 40 + 10) * 3 + c]);
        }
        using var reversed = new HealPrototype().Repair(basis, [b, a], default);
        Assert.NotEqual(codes[(15 * 40 + 10) * 3], Read(reversed)[(15 * 40 + 10) * 3]);
        using var bypass = new HealPrototype().Repair(basis, [], default);
        Assert.Equal(input, Read(bypass));
    }

    [Fact]
    public void SourceDiscClampsInFrameCoordinatesBeforePixelCenterMapping()
    {
        var input = Enumerable.Range(0, 100 * 60 * 3).Select(i => (ushort)(i / 3 % 100 * 400)).ToArray();
        using var basis = RenderPipelineTestSupport.CreateBase(input, height: 60);
        using var repaired = new HealPrototype().Repair(basis,
            [new(.5, .5, 0, .5, .1, IsClone: true, Feather: 0)], default);
        // Clamped source u=.1 gives x=9.5; destination pixel x=50 is +.5 from its center.
        Assert.Equal((ushort)4000, Read(repaired)[(30 * 100 + 50) * 3]);
    }

    [Fact]
    public void HighlightRepairThenMinusTwoEvHasNoCeilingPlateau()
    {
        const int width = 160, height = 100;
        var values = new ushort[width * height * 3];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) for (var c = 0; c < 3; c++)
            values[(y * width + x) * 3 + c] = (ushort)((x < 80 ? 18000 : 46000) + x * 50 + y * 20 + c * 100 +
                33000 * Math.Exp(-((x - 44.3) * (x - 44.3) + (y - 49.5) * (y - 49.5)) / 40));
        using var basis = RenderPipelineTestSupport.CreateBase(values, height: height);
        foreach (var candidate in HealCandidate.All)
        {
            var spot = new HealSpot(.51, .5, .28, .5, .10, Feather: .2);
            using var repair = new HealPrototype().Repair(basis, [spot], candidate);
            var codes = Read(repair);
            var disc = Enumerable.Range(0, width * height).Where(p =>
                Math.Pow(p % width + .5 - spot.U * width, 2) + Math.Pow(p / width + .5 - spot.V * height, 2) < 16 * 16).ToArray();
            Assert.All(disc, p => Assert.InRange(codes[p * 3], (ushort)1, (ushort)65534));
            Assert.True(disc.Select(p => codes[p * 3]).Distinct().Count() > 100);
            using var exposed = new RenderPipeline().Render(new(repair, new EditSettings { Exposure = -2 },
                RenderIntent.Export, null, new(false, false)));
            var rendered = RenderPipelineTestSupport.ReadPixels(exposed.Image);
            Assert.True(disc.Select(p => rendered[p * 3]).Distinct().Count() > 100);
        }
    }

    [Fact]
    public void WorkloadsPinCapRadiusAndHalfAreaOverlap()
    {
        var s = HealWorkloads.S64();
        Assert.Equal(48, s.Count(p => !p.IsClone)); Assert.Equal(16, s.Count(p => p.IsClone));
        Assert.Equal(.005, s.Min(p => p.Radius)); Assert.Equal(.04, s.Max(p => p.Radius));
        Assert.Contains(s, p => p.Su < .1); // Outside the centered .8-wide 3:2 crop.
        var cap = HealWorkloads.SCap(5496, 3670);
        Assert.Equal(64, cap.Length);
        foreach (var pair in cap.Zip(cap.Skip(1)))
        {
            var dx = pair.First.U - pair.Second.U;
            var dy = (pair.First.V - pair.Second.V) * 3670 / 5496;
            var distance = Math.Sqrt(dx * dx + dy * dy) / HealWorkloads.MaxRadius;
            var overlap = 2 * Math.Acos(distance / 2) - distance * Math.Sqrt(4 - distance * distance) / 2;
            Assert.Equal(.5, overlap / Math.PI, 12);
        }
        Assert.True(HealWorkloads.Area(cap) > HealWorkloads.Area(s));
    }

    [Theory]
    [InlineData(5)] [InlineData(6)] [InlineData(8)] [InlineData(12)]
    [InlineData(16)] [InlineData(24)] [InlineData(32)] [InlineData(64)]
    public void AreaLimitAdmitsExactDiscCount(int count)
    {
        var discArea = Math.PI * HealWorkloads.MaxRadius * HealWorkloads.MaxRadius;
        var area = count * discArea;
        var limited = HealWorkloads.SCap(1600, 1068, area);
        Assert.Equal(count, limited.Length);
        Assert.Equal(HealWorkloads.SCap(1600, 1068).Take(count), limited);
        Assert.Equal(count - 1, HealWorkloads.SCap(1600, 1068, Math.BitDecrement(area)).Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => HealWorkloads.SCap(1600, 1068, 0));
    }

    [Fact]
    public void DetachedDiagnosticCopyPreservesPixelsAndRepair()
    {
        var input = Enumerable.Range(0, 160 * 120 * 3).Select(i => (ushort)(i % 65536)).ToArray();
        using var basis = RenderPipelineTestSupport.CreateBase(input, height: 120);
        foreach (var formulation in Enum.GetValues<HealFormulation>())
        {
            var candidate = new HealCandidate(formulation, HealDomain.Additive);
            using var copy = HealGateTests.DetachedCopy(basis);
            Assert.Equal(input, RenderPipelineTestSupport.ReadPixels(copy));
            new HealPrototype().Apply(copy, HealWorkloads.S64(), candidate, 2);
            using var regular = new HealPrototype().Repair(basis, HealWorkloads.S64(), candidate);
            Assert.Equal(Read(regular), RenderPipelineTestSupport.ReadPixels(copy));
            Assert.Equal(input, Read(basis));
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(90)] [InlineData(180)] [InlineData(270)]
    public void BasePointsRoundTripQuarterTurnsKeystoneAndCrop(int turn)
    {
        const int w = 300, h = 200;
        var settings = new EditSettings { Rotation = turn, HorizonRotation = 3,
            Geometry = new() { Vertical = 24, Horizontal = -17 }, Crop = new() { Left = .1, Top = .1, Right = .9, Bottom = .9 } };
        var ramp = new ushort[w * h * 3];
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
        { ramp[(y * w + x) * 3] = (ushort)(x * 100); ramp[(y * w + x) * 3 + 1] = (ushort)(y * 100); }
        using var basis = RenderPipelineTestSupport.CreateBase(ramp, height: h);
        using var geometry = RenderGeometry.Apply(basis.Pixels, settings, out var trace);
        var rendered = RenderPipelineTestSupport.ReadPixels(geometry);
        foreach (var spot in HealWorkloads.S64())
        {
            var x = spot.U * w - .5; var y = spot.V * h - .5;
            var rotated = turn switch { 90 => (h - 1 - y, x), 180 => (w - 1 - x, h - 1 - y),
                270 => (y, w - 1 - x), _ => (x, y) };
            var projected = trace.Map.MapForward(rotated.Item1, rotated.Item2);
            var displayX = projected.X - trace.CropX; var displayY = projected.Y - trace.CropY;
            var inverse = trace.Map.MapInverse(displayX + trace.CropX, displayY + trace.CropY);
            var ix = (int)Math.Round(displayX); var iy = (int)Math.Round(displayY);
            if (ix >= 0 && iy >= 0 && ix < trace.Width && iy < trace.Height)
            {
                var pixel = (iy * trace.Width + ix) * 3;
                Assert.InRange(Math.Abs(rendered[pixel] / 100d - x), 0, 1.1);
                Assert.InRange(Math.Abs(rendered[pixel + 1] / 100d - y), 0, 1.1);
            }
            var original = turn switch { 90 => (inverse.Y, h - 1 - inverse.X),
                180 => (w - 1 - inverse.X, h - 1 - inverse.Y), 270 => (w - 1 - inverse.Y, inverse.X),
                _ => (inverse.X, inverse.Y) };
            Assert.InRange(Math.Abs(original.Item1 - x), 0, 1e-9);
            Assert.InRange(Math.Abs(original.Item2 - y), 0, 1e-9);
        }
    }

    private static ushort[] Read(BaseImage basis) => RenderPipelineTestSupport.ReadPixels(basis.Pixels);
}

