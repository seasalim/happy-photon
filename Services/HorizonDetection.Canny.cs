using System.Runtime.CompilerServices;

namespace HappyPhoton.Services;

public static partial class HorizonDetection
{
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe List<Edge> Canny(double[] gx, double[] gy, int width, int height)
    {
        var magnitude = new double[gx.Length];
        var distribution = new double[gx.Length];
        var count = 0;

        for (var y = Border; y < height - Border; y++)
        {
            for (var x = Border; x < width - Border; x++)
            {
                var i = y * width + x;
                magnitude[i] = Math.Sqrt(gx[i] * gx[i] + gy[i] * gy[i]);
                if (magnitude[i] > 1d / ushort.MaxValue) distribution[count++] = magnitude[i];
            }
        }

        if (count == 0) return [];

        var high = SelectMagnitude(distribution, count, (int)((count - 1) * HighPercentile));
        var low = high * LowThresholdRatio;
        var state = new byte[gx.Length];
        var pending = new int[gx.Length];
        var pendingCount = Suppress(gx, gy, magnitude, width, height, high, low, state, pending);

        fixed (double* samples = magnitude)
        {
            while (pendingCount > 0)
            {
                var i = pending[--pendingCount];

                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var next = i + dy * width + dx;
                        if (state[next] != 1) continue;

                        state[next] = 2;
                        pending[pendingCount++] = next;
                    }
                }
            }

            var edges = new List<Edge>();

            for (var y = Border + 1; y < height - Border - 1; y++)
            {
                for (var x = Border + 1; x < width - Border - 1; x++)
                {
                    var i = y * width + x;
                    if (state[i] != 2) continue;

                    var ax = Math.Abs(gx[i]);
                    var ay = Math.Abs(gy[i]);
                    var vertical = ax >= ay;
                    // atan(1/8) is wider than Window. Discard only obvious non-voters
                    // before atan2; their pixels still participate in hysteresis above.
                    if (Math.Min(ax, ay) > Math.Max(ax, ay) * .125) continue;

                    var tilt = Fold(Math.Atan2(gy[i], gx[i]) * 180 / Math.PI);
                    if (Math.Abs(tilt) > Window) continue;

                    var dx = gx[i] / magnitude[i];
                    var dy = gy[i] / magnitude[i];
                    var before = Sample(samples, width, x - dx, y - dy);
                    var after = Sample(samples, width, x + dx, y + dy);
                    var offset = .5 * (before - after) / (before - 2 * magnitude[i] + after);
                    edges.Add(new(x + offset * dx, y + offset * dy, tilt, vertical));
                }
            }

            return edges;
        }
    }

    // Positive finite doubles have the same numeric and unsigned-bit ordering. Refine
    // the selected histogram bucket until it identifies the exact order statistic.
    // This preserves threshold ties without sorting every gradient or quantizing it.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal static double SelectMagnitude(double[] values, int count, int rank)
    {
        var histogram = new int[1 << 16];

        for (var shift = 48; shift >= 0; shift -= 16)
        {
            Array.Clear(histogram);

            for (var i = 0; i < count; i++)
            {
                var bucket = (int)((BitConverter.DoubleToUInt64Bits(values[i]) >> shift) & 65535);
                histogram[bucket]++;
            }

            var selected = 0;

            while (rank >= histogram[selected])
            {
                rank -= histogram[selected++];
            }

            var retained = 0;

            for (var i = 0; i < count; i++)
            {
                var bucket = (int)((BitConverter.DoubleToUInt64Bits(values[i]) >> shift) & 65535);
                if (bucket == selected) values[retained++] = values[i];
            }

            count = retained;
            if (count == 1) break;
        }

        return values[0];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe double Sample(double* plane, int width, double x, double y)
    {
        var ix = (int)x;
        var iy = (int)y;
        var fx = x - ix;
        var fy = y - iy;
        var i = iy * width + ix;

        return (plane[i] * (1 - fx) + plane[i + 1] * fx) * (1 - fy) +
            (plane[i + width] * (1 - fx) + plane[i + width + 1] * fx) * fy;
    }
}
