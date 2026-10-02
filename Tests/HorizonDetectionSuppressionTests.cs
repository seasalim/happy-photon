using System.Reflection;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class HorizonDetectionSuppressionTests
{
    [Theory]
    [InlineData(37, 39)]
    [InlineData(40, 47)]
    [InlineData(129, 91)]
    public void SuppressionMatchesScalarInterpolationIncludingWeakLanesAndTails(int width, int height)
    {
        var random = new Random(316);
        var gx = new double[width * height];
        var gy = new double[gx.Length];
        var magnitude = new double[gx.Length];

        for (var i = 0; i < gx.Length; i++)
        {
            gx[i] = i % 7 == 0 ? 0 : random.NextDouble() - .5;
            gy[i] = i % 5 == 0 ? 0 : random.NextDouble() - .5;
            magnitude[i] = Math.Sqrt(gx[i] * gx[i] + gy[i] * gy[i]);
        }

        var state = new byte[gx.Length];
        var pending = new int[gx.Length];
        var method = typeof(HorizonDetection).GetMethod("Suppress", BindingFlags.NonPublic | BindingFlags.Static)!;
        var count = (int)method.Invoke(null, [gx, gy, magnitude, width, height, .3, .1, state, pending])!;
        var expected = new byte[gx.Length];
        var seeds = new List<int>();

        for (var y = 17; y < height - 17; y++)
        {
            for (var x = 17; x < width - 17; x++)
            {
                var i = y * width + x;
                var m = magnitude[i];
                if (m < .1) continue;

                var dx = gx[i] / m;
                var dy = gy[i] / m;
                if (m < Sample(x - dx, y - dy) || m <= Sample(x + dx, y + dy)) continue;

                expected[i] = m >= .3 ? (byte)2 : (byte)1;
                if (expected[i] == 2) seeds.Add(i);
            }
        }

        Assert.Equal(expected, state);
        Assert.Equal(seeds, pending.Take(count));

        double Sample(double x, double y)
        {
            var ix = (int)x;
            var iy = (int)y;
            var fx = x - ix;
            var fy = y - iy;
            var i = iy * width + ix;

            return (magnitude[i] * (1 - fx) + magnitude[i + 1] * fx) * (1 - fy) +
                (magnitude[i + width] * (1 - fx) + magnitude[i + width + 1] * fx) * fy;
        }
    }
}
