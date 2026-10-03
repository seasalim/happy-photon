using ImageMagick;

namespace HappyPhoton.Tests;

// Frozen test-only tier evaluation. No detector, region, label, or target angle
// participates: area-average Rec.2020 luminance, sRGB encode, then one strongest
// downward transition per column in the upper two thirds. All-pairs Theil-Sen
// and median intercept are deterministic; residuals are vertical working pixels.
internal static class StraightenGateWp3SkylineOracle
{
    internal const int LongEdge = 1024;

    internal const double InlierResidual = 3;

    internal sealed record Fit(int Width, int Height, double Share, double Span,
        double ContentAngle, double Slope, double Intercept, int Inliers);

    internal static Fit Evaluate(MagickImage image)
    {
        var scale = Math.Min(1, LongEdge / (double)Math.Max(image.Width, image.Height));
        var width = (int)Math.Round(image.Width * scale);
        var height = (int)Math.Round(image.Height * scale);
        var plane = ReadPlane(image, width, height);
        var points = new double[width];

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

            for (var y = Math.Max(1, peak - 3); y <= Math.Min(height - 2, peak + 3); y++)
            {
                var gradient = Math.Max(0, plane[(y - 1) * width + x] - plane[(y + 1) * width + x]);
                weighted += y * gradient;
                weight += gradient;
            }

            points[x] = weight > 0 ? weighted / weight : peak;
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

        var slope = FinishingGateSupport.Median(slopes);
        var intercept = FinishingGateSupport.Median(points.Select((y, x) => y - slope * x).ToArray());
        var inliers = Enumerable.Range(0, width).Where(x =>
            Math.Abs(points[x] - intercept - slope * x) <= InlierResidual).ToArray();
        var span = inliers.Length == 0 ? 0 : (inliers[^1] - inliers[0]) / (double)width;
        var baseSlope = slope * (image.Height / (double)height) / (image.Width / (double)width);

        return new(width, height, inliers.Length / (double)width, span,
            Math.Atan(baseSlope) * 180 / Math.PI, slope, intercept, inliers.Length);
    }

    private static double[] ReadPlane(MagickImage image, int width, int height)
    {
        var pixels = RenderPipelineTestSupport.ReadPixels(image);
        var sourceWidth = (int)image.Width;
        var sourceHeight = (int)image.Height;
        var sx = sourceWidth / (double)width;
        var sy = sourceHeight / (double)height;
        var plane = new double[width * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sum = 0d;

                for (var row = (int)(y * sy); row < Math.Min(sourceHeight, (int)Math.Ceiling((y + 1) * sy)); row++)
                {
                    var wy = Math.Min(row + 1, (y + 1) * sy) - Math.Max(row, y * sy);

                    for (var col = (int)(x * sx); col < Math.Min(sourceWidth, (int)Math.Ceiling((x + 1) * sx)); col++)
                    {
                        var wx = Math.Min(col + 1, (x + 1) * sx) - Math.Max(col, x * sx);
                        var i = (row * sourceWidth + col) * 3;
                        sum += wx * wy * (.2627 * pixels[i] + .6780 * pixels[i + 1] + .0593 * pixels[i + 2]);
                    }
                }

                var linear = sum / (sx * sy * ushort.MaxValue);
                plane[y * width + x] = linear <= .0031308 ? 12.92 * linear :
                    1.055 * Math.Pow(linear, 1 / 2.4) - .055;
            }
        }

        return plane;
    }
}
