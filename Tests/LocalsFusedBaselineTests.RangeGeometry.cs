using HappyPhoton.Models;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsFusedBaselineTests
{
    [Theory]
    [InlineData(160, 107)]
    [InlineData(120, 160)]
    public void RangeCullingMatchesEveryFrozenR8Term(int width, int height)
    {
        using var basis = RenderPipelineTestSupport.CreateBase(new ushort[width * height * 3], true, height);
        var settings = RadialSettings(basis, true);
        var edge = Math.Max(width, height);
        var fw = width / (double)edge; var fh = height / (double)edge;
        var locals = settings.Locals!;
        var terms = locals.Select(l => RangeRadial.From(l, fw, fh)).ToArray();
        for (var p = 0; p < width * height; p++)
        {
            var active = RangeActive(terms, p, width, edge);
            for (var i = 0; i < terms.Length; i++)
                Assert.Equal(RadialWeight(locals[i], (p % width + .5) / edge,
                    (p / width + .5) / edge, fw, fh) > 0, (active & (1 << i)) != 0);
        }
    }
    // The frozen R8 terms have zero rotation and inside polarity. Hoist their geometry,
    // but retain the same smoothstep >0 decision as RadialWeight/production.
    private readonly record struct RangeRadial(double Cx, double Cy, double Rx, double Ry, double Feather)
    {
        internal bool Contains(double x, double y)
        {
            var a = (x - Cx) / Rx; var b = (y - Cy) / Ry;
            var square = a * a + b * b;
            if (square >= 1) return false;
            var t = Math.Clamp((Math.Sqrt(square) - (1 - Feather)) / Feather, 0, 1);
            return 1 - t * t * (3 - 2 * t) > 0;
        }
        internal static RangeRadial From(LocalAdjustment local, double fw, double fh)
        {
            Assert.Equal(0, local.Angle); Assert.False(local.Outside);
            return new(local.Cu * fw, local.Cv * fh, local.Rx, local.Ry, local.Feather);
        }
    }

    private static byte RangeActive(RangeRadial[] terms, int p, int width, int edge)
    {
        var x = (p % width + .5) / edge; var y = (p / width + .5) / edge;
        byte active = 0;
        for (var i = 0; i < terms.Length; i++)
            if (terms[i].Contains(x, y)) active |= (byte)(1 << i);
        return active;
    }
}
