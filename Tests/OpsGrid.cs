namespace HappyPhoton.Tests;

// Cell-centred area averages and bilinear reconstruction. Reductions have a fixed
// scan order within each cell, independent of how cells are assigned to workers.
internal sealed record OpsGrid(int Width, int Height, double[] Values)
{
    internal static OpsGrid Reduce(int width, int height, int edge, Func<int, double> value,
        int workers = 1, CancellationToken cancellation = default)
    {
        var w = Math.Max(1, (int)Math.Round(width * Math.Min(1, edge / (double)Math.Max(width, height))));
        var h = Math.Max(1, (int)Math.Round(height * Math.Min(1, edge / (double)Math.Max(width, height))));
        var result = new double[w * h];
        Parallel.For(0, h, new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = cancellation }, cy =>
        {
            var top = cy * height / (double)h; var bottom = (cy + 1) * height / (double)h;
            for (var cx = 0; cx < w; cx++)
            {
                var left = cx * width / (double)w; var right = (cx + 1) * width / (double)w;
                double sum = 0;
                for (var y = (int)top; y < Math.Ceiling(bottom); y++)
                for (var x = (int)left; x < Math.Ceiling(right); x++)
                    sum += value(y * width + x) * (Math.Min(y + 1, bottom) - Math.Max(y, top)) *
                        (Math.Min(x + 1, right) - Math.Max(x, left));
                result[cy * w + cx] = sum / ((right - left) * (bottom - top));
            }
        });
        return new(w, h, result);
    }

    internal double Sample(double u, double v)
    {
        var x = Math.Clamp(u * Width - .5, 0, Width - 1); var y = Math.Clamp(v * Height - .5, 0, Height - 1);
        var ix = (int)x; var iy = (int)y; var nx = Math.Min(ix + 1, Width - 1); var ny = Math.Min(iy + 1, Height - 1);
        var a = Values[iy * Width + ix] * (1 - (x - ix)) + Values[iy * Width + nx] * (x - ix);
        var b = Values[ny * Width + ix] * (1 - (x - ix)) + Values[ny * Width + nx] * (x - ix);
        return a * (1 - (y - iy)) + b * (y - iy);
    }

    internal OpsGrid Blur(double[] kernel)
    {
        var r = kernel.Length / 2; var temp = new double[Values.Length]; var result = new double[Values.Length];
        for (var y = 0; y < Height; y++) for (var x = 0; x < Width; x++)
        for (var k = -r; k <= r; k++) temp[y * Width + x] += kernel[k + r] * Values[y * Width + Math.Clamp(x + k, 0, Width - 1)];
        for (var y = 0; y < Height; y++) for (var x = 0; x < Width; x++)
        for (var k = -r; k <= r; k++) result[y * Width + x] += kernel[k + r] * temp[Math.Clamp(y + k, 0, Height - 1) * Width + x];
        return new(Width, Height, result);
    }

    internal static double[] Gaussian(double sigma)
    {
        var r = Math.Max(1, (int)Math.Ceiling(3 * sigma));
        var taps = Enumerable.Range(-r, r * 2 + 1).Select(x => Math.Exp(-x * x / (2 * sigma * sigma))).ToArray();
        var sum = taps.Sum(); return taps.Select(x => x / sum).ToArray();
    }
}
