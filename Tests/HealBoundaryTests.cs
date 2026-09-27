using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class HealBoundaryTests
{
    [Fact]
    public void FootprintIsNormalizedAndSpansOnePixelAtMinimumPreviewRadius()
    {
        Assert.Equal(1d / 3, RenderRepairs.BoundaryHalfWidthInRadii);
        Assert.True(1600 * HealWorkloads.MinRadius * RenderRepairs.BoundaryHalfWidthInRadii >= 1);
        // Every footprint square lies outside the destination disc (touching it at 45 degrees),
        // so a blemish filling its spot never enters the boundary estimate.
        var h = RenderRepairs.BoundaryHalfWidthInRadii; var reach = 1 + RenderRepairs.BoundaryOffsetInRadii;
        var nearest = Enumerable.Range(0, RenderRepairs.RingSamples).Min(k =>
        {
            var (x, y) = (reach * Math.Cos(2 * Math.PI * k / RenderRepairs.RingSamples), reach * Math.Sin(2 * Math.PI * k / RenderRepairs.RingSamples));
            var (px, py) = (Math.Clamp(0, x - h, x + h), Math.Clamp(0, y - h, y + h));
            return Math.Sqrt(px * px + py * py);
        });
        Assert.InRange(nearest, 1 - 1e-12, 1 + 1e-12);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(4)]
    public void NormalizedBilinearFieldHasSameBoundaryMeanAtEveryResolution(int scale)
    {
        var width = 128 * scale; var height = 96 * scale;
        static double Field(double x, double y) => 8192 + 64 * x + 32 * y + 2 * x * y;
        var pixels = Enumerable.Range(0, width * height * 3).Select(i =>
            (ushort)Math.Round(Field((i / 3 % width + .5) / scale, (i / 3 / width + .5) / scale))).ToArray();
        foreach (var angle in Enumerable.Range(0, 32))
        {
            var x = 60 + 12 * Math.Cos(angle * Math.PI / 16);
            var y = 40 + 12 * Math.Sin(angle * Math.PI / 16);
            var actual = HealProductionStage.BoundaryMean(pixels, width, height,
                x * scale - .5, y * scale - .5, 4 * scale, 0) * 65535;
            // A symmetric square's integral of a bilinear polynomial is its center value.
            Assert.InRange(Math.Abs(actual - Field(x, y)), 0, .50000001);
        }
    }

    [Fact]
    public void FootprintAveragesPixelNoiseAndIntegratesClampedImageEdges()
    {
        const int width = 64, height = 48;
        var checker = Enumerable.Range(0, width * height * 3).Select(i =>
            (ushort)(30000 + ((i / 3 % width + i / 3 / width) % 2 == 0 ? 12000 : -12000))).ToArray();
        // Four full checkerboard periods: exact cancellation at arbitrary subpixel phase.
        foreach (var phase in new[] { 0d, .17, .5 })
            Assert.Equal(30000, HealProductionStage.BoundaryMean(checker, width, height,
                30 + phase, 20 + phase, 4, 0) * 65535, 8);
        var ramp = Enumerable.Range(0, width * height * 3).Select(i =>
            (ushort)(1000 + 100 * (i / 3 % width) + 200 * (i / 3 / width))).ToArray();
        // mean(max(x,0)) over [-2,2] = 1/2; y remains interior and affine.
        Assert.Equal(1700, HealProductionStage.BoundaryMean(ramp, width, height, 0, 3.25, 2, 0) * 65535, 8);
    }

    [Fact]
    public void RowPrefixIntegralMatchesDirectQuadratureIncludingClampedEdges()
    {
        var random = new Random(277);
        foreach (var (width, height) in new[] { (1, 1), (1, 9), (9, 1), (37, 23), (96, 64) })
        {
            var pixels = Enumerable.Range(0, width * height * 3).Select(_ => (ushort)random.Next(65536)).ToArray();
            for (var trial = 0; trial < 200; trial++)
            {
                var halfWidth = 1.0667 + random.NextDouble() * 12;
                // Centers range beyond the box so footprints exercise edge extension.
                var x = -halfWidth + random.NextDouble() * (width + 2 * halfWidth);
                var y = -halfWidth + random.NextDouble() * (height + 2 * halfWidth);
                var c = random.Next(3);
                Assert.Equal(DirectMean(pixels, width, height, x, y, halfWidth, c),
                    HealProductionStage.BoundaryMean(pixels, width, height, x, y, halfWidth, c), 1e-9);
            }
        }
    }

    // The turn-4 reference: midpoint quadrature per bilinear cell (exact), O(area).
    private static double DirectMean(ushort[] pixels, int width, int height,
        double x, double y, double halfWidth, int channel)
    {
        double left = x - halfWidth, right = x + halfWidth, top = y - halfWidth, bottom = y + halfWidth, total = 0;
        for (var iy = (int)Math.Floor(top); iy < Math.Ceiling(bottom); iy++)
        {
            var y1 = Math.Max(top, iy); var y2 = Math.Min(bottom, iy + 1);
            for (var ix = (int)Math.Floor(left); ix < Math.Ceiling(right); ix++)
            {
                var x1 = Math.Max(left, ix); var x2 = Math.Min(right, ix + 1);
                total += (x2 - x1) * (y2 - y1) * Bilinear(pixels, width, height, (x1 + x2) / 2, (y1 + y2) / 2, channel);
            }
        }
        return total / (4 * halfWidth * halfWidth) / 65535;
    }

    private static double Bilinear(ushort[] p, int w, int h, double x, double y, int c)
    {
        x = Math.Clamp(x, 0, w - 1); y = Math.Clamp(y, 0, h - 1);
        int ix = (int)x, iy = (int)y, r = Math.Min(ix + 1, w - 1), b = Math.Min(iy + 1, h - 1);
        double fx = x - ix, fy = y - iy;
        var top = p[(iy * w + ix) * 3 + c] * (1 - fx) + p[(iy * w + r) * 3 + c] * fx;
        var low = p[(b * w + ix) * 3 + c] * (1 - fx) + p[(b * w + r) * 3 + c] * fx;
        return top * (1 - fy) + low * fy;
    }
}
