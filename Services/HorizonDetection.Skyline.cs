using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace HappyPhoton.Services;

public static partial class HorizonDetection
{
    // Frozen robust-fit envelope: gentle boundaries retain 30% of all columns
    // within three working pixels, spread across at least half the frame.
    private const double SkylineShare = .30;

    private const double SkylineSpan = .5;

    private const double SkylineResidual = 3;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static SkylineDiagnostics Skyline(double[] plane, int width, int height, double sx, double sy)
    {
        var points = new double[width];
        var transitions = new bool[width];
        var peaks = new int[width];
        var strongest = new double[width];
        Array.Fill(peaks, 1);
        Array.Fill(strongest, double.NegativeInfinity);

        // Visit each column's rows in the original order, with contiguous plane reads.
        for (var y = 1; y < Math.Min(height - 1, height * 2 / 3); y++)
        {
            for (var x = 0; x < width; x++)
            {
                var gradient = plane[(y - 1) * width + x] - plane[(y + 1) * width + x];

                if (gradient > strongest[x])
                {
                    strongest[x] = gradient;
                    peaks[x] = y;
                }
            }
        }

        for (var x = 0; x < width; x++)
        {
            var peak = peaks[x];
            var weighted = 0d;
            var weight = 0d;

            // Match the frozen oracle's seven-row centroid around the strongest
            // bright-above transition, retaining subpixel boundary positions.
            for (var y = Math.Max(1, peak - 3); y <= Math.Min(height - 2, peak + 3); y++)
            {
                var gradient = Math.Max(0, plane[(y - 1) * width + x] - plane[(y + 1) * width + x]);
                weighted += y * gradient;
                weight += gradient;
            }

            points[x] = weight > 0 ? weighted / weight : peak;
            transitions[x] = weight > 0;
        }

        var count = width * (width - 1) / 2;
        var slopes = ArrayPool<double>.Shared.Rent(count);
        var intercepts = ArrayPool<double>.Shared.Rent(width);

        try
        {
            var index = 0;

            for (var x = 0; x < width; x++)
            {
                var other = x + 1;
                var distances = Vector256.Create(1d, 2d, 3d, 4d);

                for (; Vector256.IsHardwareAccelerated && other <= width - 4; other += 4)
                {
                    var slopesVector = (Vector256.LoadUnsafe(ref points[other]) - Vector256.Create(points[x])) /
                        distances;
                    slopesVector.StoreUnsafe(ref slopes[index]);
                    index += 4;
                    distances += Vector256.Create(4d);
                }

                for (; other < width; other++)
                {
                    slopes[index++] = (points[other] - points[x]) / (other - x);
                }
            }

            var slope = Median(slopes, count);

            for (var x = 0; x < width; x++)
            {
                intercepts[x] = points[x] - slope * x;
            }

            var intercept = Median(intercepts, width);
            var inliers = Enumerable.Range(0, width).Where(x =>
                transitions[x] && Math.Abs(points[x] - intercept - slope * x) <= SkylineResidual).ToArray();
            var span = inliers.Length == 0 ? 0 : (inliers[^1] - inliers[0]) / (double)width;

            return new(inliers.Length / (double)width, span, Math.Atan(slope * sy / sx) * 180 / Math.PI);
        }
        finally
        {
            ArrayPool<double>.Shared.Return(slopes);
            ArrayPool<double>.Shared.Return(intercepts);
        }
    }

    // Deterministic order selection computes the exact all-pairs Theil-Sen median
    // without ordering the half-million slopes at the 1024-pixel working scale.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe double Median(double[] values, int count)
    {
        // The frozen oracle chooses the upper order statistic for even counts.
        var target = count / 2;

        fixed (double* samples = values)
        {
            var left = 0;
            var right = count - 1;

            while (left < right)
            {
                var pivot = samples[(left + right) / 2];
                var low = left;
                var high = right;

                while (low <= high)
                {
                    // The pivot is in this partition, so both scans stop inside it.
                    while (samples[low] < pivot) low++;
                    while (samples[high] > pivot) high--;

                    if (low <= high)
                    {
                        (samples[low], samples[high]) = (samples[high], samples[low]);
                        low++;
                        high--;
                    }
                }

                if (target <= high) right = high;
                else if (target >= low) left = low;
                else break;
            }

            return samples[target];
        }
    }
}
