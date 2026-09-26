namespace HappyPhoton.Tests;

// Exact integral of a box's clamped bilinear reconstruction over axis-aligned squares.
// Per-row trapezoid prefixes make each row's horizontal integral O(1), so a footprint
// costs O(rows) instead of O(rows x columns). Values stay in raw Q16 code units.
internal readonly struct HealBoundaryRows
{
    private readonly ushort[] pixels;
    private readonly double[] prefix;
    private readonly int width, height;

    // prefix must hold width x height x 3 values; it is fully overwritten.
    internal HealBoundaryRows(ushort[] pixels, int width, int height, double[] prefix, int workers)
    {
        this.pixels = pixels; this.prefix = prefix; this.width = width; this.height = height;
        var local = this;
        Parallel.For(0, height, new ParallelOptions { MaxDegreeOfParallelism = workers }, row =>
        {
            var start = row * local.width * 3;
            for (var c = 0; c < 3; c++)
            {
                double total = 0;
                local.prefix[start + c] = 0;
                for (var i = 1; i < local.width; i++)
                {
                    total += (local.pixels[start + (i - 1) * 3 + c] + local.pixels[start + i * 3 + c]) / 2d;
                    local.prefix[start + i * 3 + c] = total;
                }
            }
        });
    }

    // Mean over [x ± halfWidth] x [y ± halfWidth] in box pixel coordinates, normalized to [0,1].
    internal double Mean(double x, double y, double halfWidth, int channel)
    {
        double left = x - halfWidth, right = x + halfWidth, top = y - halfWidth, bottom = y + halfWidth;
        double total = 0;
        for (var j = (int)Math.Floor(top); j < Math.Ceiling(bottom); j++)
        {
            var y1 = Math.Max(top, j); var y2 = Math.Min(bottom, j + 1);
            // The vertical interpolant is linear inside [j, j+1], so its midpoint is exact.
            var mid = Math.Clamp((y1 + y2) / 2, 0, height - 1);
            var lower = (int)mid; var t = mid - lower; var upper = Math.Min(lower + 1, height - 1);
            var rows = (1 - t) * Row(lower, left, right, channel) + t * Row(upper, left, right, channel);
            total += (y2 - y1) * rows;
        }
        return total / (4 * halfWidth * halfWidth) / 65535;
    }

    private double Row(int row, double left, double right, int channel) =>
        Cumulative(row, right, channel) - Cumulative(row, left, channel);

    // Integral from 0 to x of the row's clamped linear interpolant (negative for x < 0).
    private double Cumulative(int row, double x, int channel)
    {
        var start = row * width * 3 + channel;
        if (x <= 0) return x * pixels[start];
        var last = width - 1;
        if (x >= last) return prefix[start + last * 3] + (x - last) * pixels[start + last * 3];
        var i = (int)x; var t = x - i;
        double a = pixels[start + i * 3], b = pixels[start + (i + 1) * 3];
        return prefix[start + i * 3] + t * a + t * t / 2 * (b - a);
    }
}
