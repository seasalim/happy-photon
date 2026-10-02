using ImageMagick;

namespace HappyPhoton.Tests;

// A tube around one physical edge. Along/Across are x/y for a horizontal
// edge and y/x for a vertical edge; coordinates refer to base pixel centers.
internal sealed record StraightenGateRegion(string Name, bool Vertical, double Start,
    double End, double AcrossStart, double AcrossEnd, double Radius, int AlongRadius = 0,
    StraightenGateSpan[]? VisibleSpans = null);

internal sealed record StraightenGateSpan(double Start, double End);

internal sealed record StraightenGateFit(double ContentAngle, double Slope, double Intercept,
    int Samples, double RmsPixels)
{
    internal double At(double along) => Intercept + Slope * along;
}

internal static class StraightenGateOracle
{
    // Independent of geometry transforms and the future detector: locate a
    // contrast transition per scanline, centroid its finite-difference response
    // to sub-pixel precision, then ordinary least-squares fit positions.
    // No orientation histogram, downscale, or supplied target angle.
    internal static StraightenGateFit Fit(MagickImage image, StraightenGateRegion roi)
    {
        var values = RenderPipelineTestSupport.ReadPixels(image);
        var width = (int)image.Width;
        var height = (int)image.Height;
        var limit = roi.Vertical ? width : height;
        var points = new List<(double Along, double Across)>();
        double Luma(int along, int across)
        {
            var x = roi.Vertical ? across : along;
            var y = roi.Vertical ? along : across;
            var i = (y * width + x) * 3;

            var sum = 0d;

            for (var offset = -roi.AlongRadius; offset <= roi.AlongRadius; offset++)
            {
                var index = i + offset * (roi.Vertical ? width * 3 : 3);
                sum += .2627 * values[index] + .6780 * values[index + 1] + .0593 * values[index + 2];
            }

            return sum / (2 * roi.AlongRadius + 1);
        }

        for (var along = (int)Math.Ceiling(roi.Start); along <= (int)Math.Floor(roi.End); along++)
        {
            if (roi.VisibleSpans is { } spans && !spans.Any(s => along >= s.Start && along <= s.End)) continue;

            var expected = roi.AcrossStart + (roi.AcrossEnd - roi.AcrossStart) *
                (along - roi.Start) / (roi.End - roi.Start);
            var first = Math.Max(5, (int)Math.Floor(expected - roi.Radius));
            var last = Math.Min(limit - 6, (int)Math.Ceiling(expected + roi.Radius));
            var peak = first;
            var strongest = 0d;

            for (var across = first; across <= last; across++)
            {
                var gradient = Luma(along, across + 1) - Luma(along, across - 1);

                if (Math.Abs(gradient) > Math.Abs(strongest))
                {
                    strongest = gradient;
                    peak = across;
                }
            }

            if (Math.Abs(strongest) < 32) continue;

            var weighted = 0d;
            var weight = 0d;

            for (var across = peak - 3; across <= peak + 3; across++)
            {
                var gradient = Math.Max(0, Math.Sign(strongest) *
                    (Luma(along, across + 1) - Luma(along, across - 1)));
                weighted += across * gradient;
                weight += gradient;
            }

            points.Add((along, weighted / weight));
        }

        if (points.Count < 20) throw new InvalidOperationException($"Too few edge samples: {roi.Name}");

        var meanAlong = points.Average(p => p.Along);
        var meanAcross = points.Average(p => p.Across);
        var slope = points.Sum(p => (p.Along - meanAlong) * (p.Across - meanAcross)) /
            points.Sum(p => (p.Along - meanAlong) * (p.Along - meanAlong));
        var intercept = meanAcross - slope * meanAlong;
        var rms = Math.Sqrt(points.Average(p => Math.Pow(p.Across - intercept - slope * p.Along, 2)));
        var angle = Math.Atan(slope) * 180 / Math.PI * (roi.Vertical ? -1 : 1);

        return new(angle, slope, intercept, points.Count, rms);
    }
}

