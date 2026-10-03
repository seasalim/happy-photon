using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace HappyPhoton.Services;

public static partial class HorizonDetection
{
    // One linear Q16 step expands by at most the sRGB toe slope, 12.92.
    // Sub-quantum derivatives at extrema are rounding noise, not directional evidence.
    private const double OrientationQuantum = 12.92 / ushort.MaxValue;

    // A symmetric one-degree prior (G4's near-level bound) suppresses diffuse
    // texture peaks while retaining the full window and selecting a supported bin.
    private const double OrientationPriorSigma = 1;

    // These are the existing angle bins expressed as slope intervals. A wide
    // floating-point guard leaves all boundary rounding to the original Atan2 path.
    private const double OrientationRatioGuard = 1e-12;

    private static readonly double[] OrientationLowerRatios = Enumerable.Range(0, (int)(2 * RotationLimit / BinWidth) + 1)
        .Select(bin => Math.Tan(Math.Max(-RotationLimit, (bin - .5) * BinWidth - RotationLimit) * Math.PI / 180)).ToArray();

    private static readonly double[] OrientationUpperRatios = Enumerable.Range(0, (int)(2 * RotationLimit / BinWidth) + 1)
        .Select(bin => Math.Tan(Math.Min(RotationLimit, (bin + .5) * BinWidth - RotationLimit) * Math.PI / 180)).ToArray();

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static Result Orientation(double[] gx, double[] gy, double[] magnitude,
        int width, int height, double sx, double sy)
    {
        var histogram = new double[(int)(2 * RotationLimit / BinWidth) + 1];
        var total = 0d;
        const int lanes = 4;

        for (var y = Border; y < height - Border; y++)
        {
            var x = Border;

            for (; Vector256.IsHardwareAccelerated && x <= width - Border - lanes; x += lanes)
            {
                var i = y * width + x;
                var weights = Vector256.Max(Vector256<double>.Zero,
                    Vector256.LoadUnsafe(ref magnitude[i]) - Vector256.Create(OrientationQuantum));
                var ax = Vector256.Abs(Vector256.LoadUnsafe(ref gx[i])) * Vector256.Create(sy);
                var ay = Vector256.Abs(Vector256.LoadUnsafe(ref gy[i])) * Vector256.Create(sx);
                // .088 is wider than tan(5 degrees); all actual voters survive.
                var voters = (Vector256.GreaterThan(weights, Vector256<double>.Zero) &
                    (Vector256.LessThanOrEqual(ax, ay * Vector256.Create(.088)) |
                        Vector256.LessThanOrEqual(ay, ax * Vector256.Create(.088)))).ExtractMostSignificantBits();

                // Preserve the original scalar accumulation and voting order.
                total += weights.GetElement(0);
                total += weights.GetElement(1);
                total += weights.GetElement(2);
                total += weights.GetElement(3);

                while (voters != 0)
                {
                    var sample = i + BitOperations.TrailingZeroCount(voters);
                    VoteOrientation(gx[sample] / sx, gy[sample] / sy,
                        magnitude[sample] - OrientationQuantum, histogram);
                    voters &= voters - 1;
                }
            }

            for (; x < width - Border; x++)
            {
                var i = y * width + x;
                var weight = magnitude[i] - OrientationQuantum;
                if (weight <= 0) continue;

                total += weight;
                var ax = Math.Abs(gx[i]) * sy;
                var ay = Math.Abs(gy[i]) * sx;
                // .088 is wider than tan(5 degrees): reject only certain non-voters.
                // Keep their weight in total, and preserve the exact angle/bin calculation.
                if (ax > ay * .088 && ay > ax * .088) continue;

                var dx = gx[i] / sx;
                var dy = gy[i] / sy;
                VoteOrientation(dx, dy, weight, histogram);
            }
        }

        for (var bin = 0; bin < histogram.Length; bin++)
        {
            var angle = bin * BinWidth - RotationLimit;
            histogram[bin] *= Math.Exp(-.5 * Math.Pow(angle / OrientationPriorSigma, 2));
        }

        var peak = histogram.Length / 2;

        for (var bin = 0; bin < histogram.Length; bin++)
        {
            if (histogram[bin] > histogram[peak]) peak = bin;
        }

        var tilt = peak * BinWidth - RotationLimit;

        return new(-tilt, total > 0 ? histogram[peak] / total : 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void VoteOrientation(double dx, double dy, double weight, double[] histogram)
    {
        var ratio = Math.Abs(dx) >= Math.Abs(dy) ? dy / dx : -dx / dy;
        // The small-angle estimate only proposes a bin; its exact tangent bounds
        // must certify membership. It never supplies an angle or changes a weight.
        var bin = (int)Math.Round((ratio * 180 / Math.PI + RotationLimit) / BinWidth);

        if ((uint)bin < (uint)histogram.Length &&
            ratio > OrientationLowerRatios[bin] + OrientationRatioGuard &&
            ratio < OrientationUpperRatios[bin] - OrientationRatioGuard)
        {
            histogram[bin] += weight;

            return;
        }

        var angle = Fold(Math.Atan2(dy, dx) * 180 / Math.PI);
        if (Math.Abs(angle) > RotationLimit) return;

        histogram[(int)Math.Round((angle + RotationLimit) / BinWidth)] += weight;
    }
}
