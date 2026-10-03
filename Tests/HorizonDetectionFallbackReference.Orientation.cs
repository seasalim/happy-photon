using HappyPhoton.Services;

namespace HappyPhoton.Tests;

// Frozen pre-optimization fallback arithmetic for bit-for-bit regression comparisons.
internal static partial class HorizonDetectionFallbackReference
{
    private const int Border = 16;

    private const double RotationLimit = 5;

    private const double BinWidth = .1;

    private static double Fold(double angle) => angle - 90 * Math.Floor((angle + 45) / 90);

    // One linear Q16 step expands by at most the sRGB toe slope, 12.92.
    // Sub-quantum derivatives at extrema are rounding noise, not directional evidence.
    private const double OrientationQuantum = 12.92 / ushort.MaxValue;

    // A symmetric one-degree prior (G4's near-level bound) suppresses diffuse
    // texture peaks while retaining the full window and selecting a supported bin.
    private const double OrientationPriorSigma = 1;

    internal static HorizonDetection.Result Orientation(double[] gx, double[] gy, int width, int height, double sx, double sy)
    {
        var histogram = new double[(int)(2 * RotationLimit / BinWidth) + 1];
        var total = 0d;

        for (var y = Border; y < height - Border; y++)
        {
            for (var x = Border; x < width - Border; x++)
            {
                var i = y * width + x;
                var dx = gx[i] / sx;
                var dy = gy[i] / sy;
                var weight = Math.Max(0, Math.Sqrt(gx[i] * gx[i] + gy[i] * gy[i]) - OrientationQuantum);
                var angle = Fold(Math.Atan2(dy, dx) * 180 / Math.PI);
                total += weight;

                if (Math.Abs(angle) > RotationLimit) continue;

                histogram[(int)Math.Round((angle + RotationLimit) / BinWidth)] += weight;
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
}

