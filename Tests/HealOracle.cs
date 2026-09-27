namespace HappyPhoton.Tests;

// Deliberately scalar, double precision, full snapshot per spot. No prototype helpers.
// Quantize only at each spot's Q16 composition boundary, as required by creation order.
internal static class HealOracle
{
    internal static ushort[] Apply(ushort[] input, int width, int height,
        IReadOnlyList<HealSpot> spots, HealCandidate candidate)
    {
        var result = (ushort[])input.Clone();
        foreach (var spot in spots)
        {
            var snapshot = result.Select(value => value / 65535d).ToArray();
            var r = HappyPhoton.Services.RepairGeometry.EffectiveRadius(spot.Radius, width, height);
            var x0 = spot.U * width - .5; var y0 = spot.V * height - .5;
            var xs = Math.Clamp(spot.Su * width, r, width - r) - .5;
            var ys = Math.Clamp(spot.Sv * height, r, height - r) - .5;
            var ring = new double[32, 3];
            for (var angle = 0; angle < 32; angle++)
            for (var channel = 0; channel < 3; channel++)
            {
                // Footprint centers sit sqrt(2)/3 radii outside the ring (see RenderRepairs).
                var dx = r * (1 + Math.Sqrt(2) / 3) * Math.Cos(angle * Math.PI / 16);
                var dy = r * (1 + Math.Sqrt(2) / 3) * Math.Sin(angle * Math.PI / 16);
                var source = BoundaryAverage(snapshot, width, height, xs + dx, ys + dy, r / 3, channel);
                var target = BoundaryAverage(snapshot, width, height, x0 + dx, y0 + dy, r / 3, channel);
                ring[angle, channel] = candidate.Domain == HealDomain.Additive ? target - source :
                    Math.Log((target + 1d / 65535) / (source + 1d / 65535));
            }
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var px = (x - x0) / r; var py = (y - y0) / r;
                var radius = Math.Sqrt(px * px + py * py);
                if (radius >= 1) continue;
                var f = radius <= 1 - spot.Feather ? 0 : (radius - 1 + spot.Feather) / spot.Feather;
                var weight = spot.Opacity * (1 - 3 * f * f + 2 * f * f * f);
                for (var channel = 0; channel < 3; channel++)
                {
                    var source = Bilinear(snapshot, width, height, xs + x - x0, ys + y - y0, channel);
                    double correction = 0, total = 0;
                    if (!spot.IsClone)
                    {
                        for (var angle = 0; angle < 32; angle++)
                        {
                            // Poisson kernel of the sampling circle, radius R = 1 + sqrt(2)/3.
                            var ringRadius = 1 + Math.Sqrt(2) / 3;
                            var distance = Math.Pow(px - ringRadius * Math.Cos(angle * Math.PI / 16), 2) +
                                Math.Pow(py - ringRadius * Math.Sin(angle * Math.PI / 16), 2);
                            var kernel = candidate.Formulation == HealFormulation.Membrane ?
                                (ringRadius * ringRadius - radius * radius) / Math.Max(1e-24, distance) : Math.Exp(-distance / .5);
                            correction += kernel * ring[angle, channel]; total += kernel;
                        }
                        correction /= total;
                        if (candidate.Domain == HealDomain.Ratio)
                            correction = (source + 1d / 65535) * (Math.Exp(correction) - 1);
                    }
                    // Half-code-plus reserve: rounding can never reach a range limit the source did not.
                    var room = Math.Max(0, (correction < 0 ? source : 1 - source) - .51 / 65535);
                    var magnitude = Math.Abs(correction);
                    // Independent algebraic form of the 7/8 knee, 3/32 reserve rule.
                    var adjusted = magnitude > 7 * room / 8 ?
                        Math.Sign(correction) * (29 * room / 32 - room * room /
                            (1024 * (magnitude - 27 * room / 32))) : correction;
                    var index = (y * width + x) * 3 + channel;
                    result[index] = checked((ushort)Math.Round(65535 *
                        ((1 - weight) * snapshot[index] + weight * (source + adjusted))));
                }
            }
        }
        return result;
    }

    // Independent cell-wise trapezoidal integral; exact for bilinear cells.
    private static double BoundaryAverage(double[] data, int w, int h,
        double x, double y, double radius, int channel)
    {
        double integral = 0;
        for (var row = (int)Math.Floor(y - radius); row < y + radius; row++)
        for (var col = (int)Math.Floor(x - radius); col < x + radius; col++)
        {
            var l = Math.Max(col, x - radius); var r = Math.Min(col + 1, x + radius);
            var t = Math.Max(row, y - radius); var b = Math.Min(row + 1, y + radius);
            var corners = Bilinear(data, w, h, l, t, channel) + Bilinear(data, w, h, r, t, channel) +
                Bilinear(data, w, h, l, b, channel) + Bilinear(data, w, h, r, b, channel);
            integral += (r - l) * (b - t) * corners / 4;
        }
        return integral / (4 * radius * radius);
    }

    private static double Bilinear(double[] data, int w, int h, double x, double y, int c)
    {
        x = Math.Clamp(x, 0, w - 1); y = Math.Clamp(y, 0, h - 1);
        var left = (int)Math.Floor(x); var top = (int)Math.Floor(y);
        double value = 0;
        for (var dy = 0; dy < 2; dy++)
        for (var dx = 0; dx < 2; dx++)
            value += data[(Math.Min(top + dy, h - 1) * w + Math.Min(left + dx, w - 1)) * 3 + c] *
                (dx == 0 ? 1 - x + left : x - left) * (dy == 0 ? 1 - y + top : y - top);
        return value;
    }
}
