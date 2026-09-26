using HappyPhoton.Services;

namespace HappyPhoton.Tests;

// Scalar double reference: direct 2-D quadrature/convolution, no prototype kernel calls.
internal static class OpsPresenceOracle
{
    internal static ushort[] Apply(ushort[] rgb, int width, int height, int nativeEdge, OpsArm arm,
        OpsClarity candidate, OpsAmountField? field = null)
    {
        if (arm.Texture == 0 && arm.Clarity == 0 && field == null) return (ushort[])rgb.Clone();
        var light = Enumerable.Range(0, width * height).Select(i => OpsWorkloads.Luma(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]) / 65535).ToArray();
        var result = (ushort[])rgb.Clone();
        var scale = Math.Max(width, height) / (double)nativeEdge;
        var weights = new SortedDictionary<int, double>();
        double[] b3 = [1d / 16, .25, .375, .25, 1d / 16];
        for (var a = -2; a <= 2; a++) for (var b = -2; b <= 2; b++) for (var c = -2; c <= 2; c++)
        {
            var pos = (a + 2 * b + 4 * c) * scale; var lo = (int)Math.Floor(pos); var fraction = pos - lo;
            var mass = b3[a + 2] * b3[b + 2] * b3[c + 2];
            weights[lo] = weights.GetValueOrDefault(lo) + mass * (1 - fraction);
            weights[lo + 1] = weights.GetValueOrDefault(lo + 1) + mass * fraction;
        }
        var grid = Reduce(light, width, height, OpsWorkloads.PyramidEdge);
        double[]? coefficient = null; double[] baseValues;
        var sigma = .01 * Math.Max(grid.Width, grid.Height);
        if (candidate == OpsClarity.Gaussian)
        {
            var radius = Math.Max(1, (int)Math.Ceiling(3 * sigma));
            var taps = Enumerable.Range(-radius, 2 * radius + 1).Select(k => Math.Exp(-.5 * k * k / (sigma * sigma))).ToArray();
            var sum = taps.Sum(); baseValues = Convolve(grid.Values, grid.Width, grid.Height, taps.Select(k => k / sum).ToArray());
        }
        else
        {
            var radius = Math.Max(1, (int)Math.Ceiling(sigma * Math.Sqrt(3)));
            var box = Enumerable.Repeat(1d / (2 * radius + 1), 2 * radius + 1).ToArray();
            var mean = Convolve(grid.Values, grid.Width, grid.Height, box);
            var second = Convolve(grid.Values.Select(v => v * v).ToArray(), grid.Width, grid.Height, box);
            var a = new double[mean.Length]; var b = new double[a.Length];
            for (var i = 0; i < a.Length; i++)
            {
                var variance = Math.Max(0, second[i] - mean[i] * mean[i]);
                a[i] = variance / (variance + .0025); b[i] = (1 - a[i]) * mean[i];
            }
            coefficient = Convolve(a, grid.Width, grid.Height, box); baseValues = Convolve(b, grid.Width, grid.Height, box);
        }
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var i = y * width + x; var l = light[i]; double delta = 0;
            if (arm.Texture != 0 || field != null)
            {
                double smooth = 0;
                foreach (var ky in weights) foreach (var kx in weights)
                    smooth += ky.Value * kx.Value * light[Math.Clamp(y + ky.Key, 0, height - 1) * width + Math.Clamp(x + kx.Key, 0, width - 1)];
                var d = l - smooth;
                delta += (arm.Texture / 100 + (field?.Texture[i] ?? 0) / 10000d) * Math.Sign(d) * Math.Max(0, Math.Abs(d) - .01);
            }
            if (arm.Clarity != 0 || field != null)
            {
                var u = (x + .5) / width; var v = (y + .5) / height;
                var smooth = Sample(baseValues, grid.Width, grid.Height, u, v) +
                    (coefficient == null ? 0 : l * Sample(coefficient, grid.Width, grid.Height, u, v));
                delta += (arm.Clarity / 100 + (field?.Clarity[i] ?? 0) / 10000d) * 4 * l * (1 - l) * Math.Clamp(l - smooth, -.04, .04);
            }
            var min = Math.Min(rgb[i * 3], Math.Min(rgb[i * 3 + 1], rgb[i * 3 + 2]));
            var max = Math.Max(rgb[i * 3], Math.Max(rgb[i * 3 + 1], rgb[i * 3 + 2]));
            delta = Math.Clamp(delta * 65535, -min, 65535 - max);
            for (var c = 0; c < 3; c++) result[i * 3 + c] = (ushort)Math.Round(rgb[i * 3 + c] + delta, MidpointRounding.AwayFromZero);
        }
        return result;
    }

    internal static OpsGrid Reduce(double[] values, int width, int height, int edge)
    {
        var scale = Math.Min(1, edge / (double)Math.Max(width, height));
        var w = Math.Max(1, (int)Math.Round(width * scale)); var h = Math.Max(1, (int)Math.Round(height * scale));
        var sums = new double[w * h]; var areas = new double[sums.Length];
        // Scatter source pixel rectangles; prototype gathers destination cells.
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        for (var cy = y * h / height; cy <= Math.Min(h - 1, (y + 1) * h / height); cy++)
        for (var cx = x * w / width; cx <= Math.Min(w - 1, (x + 1) * w / width); cx++)
        {
            var area = Math.Max(0, Math.Min(x + 1d, (cx + 1d) * width / w) - Math.Max(x, cx * (double)width / w)) *
                Math.Max(0, Math.Min(y + 1d, (cy + 1d) * height / h) - Math.Max(y, cy * (double)height / h));
            sums[cy * w + cx] += area * values[y * width + x]; areas[cy * w + cx] += area;
        }
        for (var i = 0; i < sums.Length; i++) sums[i] /= areas[i];
        return new(w, h, sums);
    }

    private static double[] Convolve(double[] values, int width, int height, double[] taps)
    {
        var output = new double[values.Length]; var radius = taps.Length / 2;
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        for (var dy = -radius; dy <= radius; dy++) for (var dx = -radius; dx <= radius; dx++)
            output[y * width + x] += taps[dy + radius] * taps[dx + radius] *
                values[Math.Clamp(y + dy, 0, height - 1) * width + Math.Clamp(x + dx, 0, width - 1)];
        return output;
    }

    internal static double Sample(double[] values, int width, int height, double u, double v)
    {
        var x = Math.Clamp(u * width - .5, 0, width - 1); var y = Math.Clamp(v * height - .5, 0, height - 1);
        double sum = 0;
        for (var row = 0; row < 2; row++) for (var col = 0; col < 2; col++)
            sum += values[Math.Min((int)y + row, height - 1) * width + Math.Min((int)x + col, width - 1)] *
                (col == 0 ? 1 - (x - (int)x) : x - (int)x) * (row == 0 ? 1 - (y - (int)y) : y - (int)y);
        return sum;
    }
}
