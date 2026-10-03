using HappyPhoton.Services;

namespace HappyPhoton.Tests;

internal static partial class HorizonDetectionFallbackReference
{
    // Frozen pre-optimization scan; full sorting independently checks the upper median.
    private const double SkylineResidual = 3;

    internal static HorizonDetection.SkylineDiagnostics Skyline(double[] plane, int width, int height, double sx, double sy)
    {
        var points = new double[width];
        var transitions = new bool[width];

        for (var x = 0; x < width; x++)
        {
            var peak = 1;
            var strongest = double.NegativeInfinity;

            for (var y = 1; y < Math.Min(height - 1, height * 2 / 3); y++)
            {
                var gradient = plane[(y - 1) * width + x] - plane[(y + 1) * width + x];

                if (gradient > strongest)
                {
                    strongest = gradient;
                    peak = y;
                }
            }

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

        var slopes = new double[width * (width - 1) / 2];
        var index = 0;

        for (var x = 0; x < width; x++)
        {
            for (var other = x + 1; other < width; other++)
            {
                slopes[index++] = (points[other] - points[x]) / (other - x);
            }
        }

        var slope = Median(slopes);
        var intercept = Median(points.Select((y, x) => y - slope * x).ToArray());
        var inliers = Enumerable.Range(0, width).Where(x =>
            transitions[x] && Math.Abs(points[x] - intercept - slope * x) <= SkylineResidual).ToArray();
        var span = inliers.Length == 0 ? 0 : (inliers[^1] - inliers[0]) / (double)width;

        return new(inliers.Length / (double)width, span, Math.Atan(slope * sy / sx) * 180 / Math.PI);
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);

        return values[values.Length / 2];
    }
}
