using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RepairGeometryTests
{
    [Theory]
    [InlineData(400, 100, .1, 40)]
    [InlineData(100, 400, .1, 40)]
    [InlineData(400, 100, .2, 50)]
    [InlineData(400, 40, .1, 20)]
    public void EffectiveRadiusAndSourceClampMatchOracleAndScale(int width, int height, double radius, double expected)
    {
        Assert.Equal(expected, RepairGeometry.EffectiveRadius(radius, width, height));
        foreach (var edge in new[] { 0d, 1d })
        {
            var repair = new Repair { Radius = radius, Su = edge, Sv = edge };
            var clamped = RepairGeometry.ClampSource(repair, width, height);
            Assert.Equal(clamped, RepairGeometry.ClampSource(repair, width * 2, height * 2));
            Assert.Equal(Math.Clamp(edge * width, expected, width - expected) - .5,
                BaseFrameMapping.ToPixel(clamped.U, width), 12);
            Assert.Equal(Math.Clamp(edge * height, expected, height - expected) - .5,
                BaseFrameMapping.ToPixel(clamped.V, height), 12);
            Assert.Equal(edge, repair.Su); Assert.Equal(edge, repair.Sv);
        }
    }

    [Theory]
    [InlineData(80, 20)]
    [InlineData(20, 80)]
    public void FourToOneCloneUsesTheSameDiscAndSourceAsTheOracle(int width, int height)
    {
        var repair = new Repair { U = .6, V = .5, Su = 0, Sv = 1, Radius = .1, Feather = 0, Type = "clone" };
        var source = RepairGeometry.ClampSource(repair, width, height);
        var radius = RepairGeometry.EffectiveRadius(repair.Radius, width, height);
        var cx = BaseFrameMapping.ToPixel(repair.U, width); var cy = BaseFrameMapping.ToPixel(repair.V, height);
        var sx = BaseFrameMapping.ToPixel(source.U, width); var sy = BaseFrameMapping.ToPixel(source.V, height);
        var input = Enumerable.Range(0, width * height * 3)
            .Select(i => (ushort)(100 * (i / 3 % width) + 100 * (i / 3 / width))).ToArray();
        var expected = (ushort[])input.Clone();
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            if (Math.Pow(x - cx, 2) + Math.Pow(y - cy, 2) >= radius * radius) continue;
            var value = (ushort)Math.Round(100 * Math.Clamp(sx + x - cx, 0, width - 1) +
                100 * Math.Clamp(sy + y - cy, 0, height - 1));
            for (var c = 0; c < 3; c++) expected[(y * width + x) * 3 + c] = value;
        }
        var actual = HealOracle.Apply(input, width, height,
            [new(repair.U, repair.V, repair.Su, repair.Sv, repair.Radius, true, 0)],
            new(HealFormulation.Membrane, HealDomain.Additive));
        Assert.All(expected.Zip(actual), p => Assert.InRange(Math.Abs(p.First - p.Second), 0, 1));
    }

    [Theory]
    [InlineData(5472, 3648, 1600, 1067)]
    [InlineData(4032, 3012, 2016, 1506)]
    public void FinalBaseMappingHasNoPhaseAtEitherDecodeSize(int fullWidth, int fullHeight, int width, int height)
    {
        foreach (var (full, preview) in new[] { (fullWidth, width), (fullHeight, height) })
        foreach (var u in new[] { 0d, .2, .5, .8, 1d })
        {
            Assert.Equal(u * preview - .5, BaseFrameMapping.ToPixel(u, preview));
            var fullPixel = BaseFrameMapping.ToPixel(u, full);
            Assert.Equal(u, BaseFrameMapping.ToNormalized(fullPixel, full), 14);
            Assert.Equal((fullPixel + .5) * preview / full - .5, BaseFrameMapping.ToPixel(u, preview), 10);
            // The frozen NCC workload samples integer centres of these same patches.
            Assert.Equal((int)(u * preview), (int)(BaseFrameMapping.ToPixel(u, preview) + .5));
        }
    }
}
