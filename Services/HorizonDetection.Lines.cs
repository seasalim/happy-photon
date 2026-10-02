using System.Runtime.CompilerServices;
using System.Numerics;

namespace HappyPhoton.Services;

public static partial class HorizonDetection
{
    internal enum CandidateRejection { None, Support, Overlap, Straightness, Fragmentation, Window }

    // Normal angles/rho and support/span use base coordinates; RMS uses working pixels.
    // The optional observer is test-only and never retains pixels or emits an image.
    internal readonly record struct CandidateDiagnostics(bool Vertical, double Theta, double Rho, int Votes,
        double Support, double Span, double GapFraction, double RmsResidual, double? FittedTheta,
        double? FittedRho, CandidateRejection Rejection, GeometryPoint SegmentStart, GeometryPoint SegmentEnd);

    private readonly record struct LineFit(double X, double Y, double Ux, double Uy,
        double Residual, double Span, double GapFraction, bool GapAligned = true, double Start = 0, double End = 0);

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static List<Line> FindLines(List<Edge> edges, int width, int height, double scale,
        double xScale, double yScale, ref StageDiagnostics stages, Action<CandidateDiagnostics>? traceCandidate)
    {
        var lines = new List<Line>();
        var radius = (int)Math.Ceiling(Math.Sqrt(width * width + height * height));
        var rhoBins = 2 * radius + 1;
        var thetaBins = (int)(2 * Window / BinWidth) + 1;
        var support = Math.Max(20, (int)Math.Ceiling(MinimumBaseSupport * scale));
        var accumulator = new int[thetaBins * rhoBins];
        var cosines = new double[thetaBins];
        var sines = new double[thetaBins];

        foreach (var vertical in new[] { false, true })
        {
            Array.Clear(accumulator);
            var family = edges.Where(e => e.Vertical == vertical).ToArray();
            var used = new bool[family.Length];
            var grid = new EdgeGrid(family, vertical, width, height);
            var xs = family.Select(e => e.X).ToArray();
            var ys = family.Select(e => e.Y).ToArray();

            for (var theta = 0; theta < thetaBins; theta++)
            {
                var radians = (-Window + theta * BinWidth + (vertical ? 0 : 90)) * Math.PI / 180;
                var cosine = Math.Cos(radians);
                var sine = Math.Sin(radians);
                cosines[theta] = cosine;
                sines[theta] = sine;
                var row = theta * rhoBins;
                var cosineVector = new Vector<double>(cosine);
                var sineVector = new Vector<double>(sine);
                var index = 0;

                for (; index <= family.Length - Vector<double>.Count; index += Vector<double>.Count)
                {
                    var rhos = Vector.Round(new Vector<double>(xs, index) * cosineVector +
                        new Vector<double>(ys, index) * sineVector);

                    for (var lane = 0; lane < Vector<double>.Count; lane++)
                    {
                        accumulator[row + (int)rhos[lane] + radius]++;
                    }
                }

                for (; index < family.Length; index++)
                {
                    var rho = (int)Math.Round(xs[index] * cosine + ys[index] * sine) + radius;
                    accumulator[row + rho]++;
                }
            }

            var candidates = new List<(int Theta, int Rho, int Votes)>();

            for (var theta = 1; theta < thetaBins - 1; theta++)
            {
                for (var rho = 1; rho < rhoBins - 1; rho++)
                {
                    var i = theta * rhoBins + rho;
                    var votes = accumulator[i];
                    // A bowed/noisy edge straddles rho bins. Half the 200px minimum
                    // seeds a fit; the gathered support must still meet the full minimum.
                    if (votes < support / 2) continue;

                    var maximum = true;

                    for (var dt = -1; dt <= 1; dt++)
                    {
                        for (var dr = -1; dr <= 1; dr++)
                        {
                            var neighbor = i + dt * rhoBins + dr;
                            if (accumulator[neighbor] > votes || neighbor < i && accumulator[neighbor] == votes)
                                maximum = false;
                        }
                    }

                    if (maximum) candidates.Add((theta, rho - radius, votes));
                }
            }

            stages.AccumulatorPeaks += candidates.Count;

            foreach (var candidate in candidates.OrderByDescending(c => c.Votes).ThenBy(c => c.Theta).ThenBy(c => c.Rho))
            {
                var tilt = -Window + candidate.Theta * BinWidth;
                var gathered = grid.Gather(cosines[candidate.Theta], sines[candidate.Theta], candidate.Rho, tilt);

                foreach (var indices in Segments(family, gathered, -sines[candidate.Theta],
                    cosines[candidate.Theta], scale))
                {
                    var rejection = CandidateRejection.None;

                    if (indices.Count < support)
                    {
                        stages.RejectedSupport++;
                        rejection = CandidateRejection.Support;
                    }
                    else if (indices.Count(i => used[i]) > indices.Count / 2)
                    {
                        // Adjacent cells sharing a majority of pixels are one line.
                        stages.RejectedOverlap++;
                        rejection = CandidateRejection.Overlap;
                    }

                    var fit = rejection == CandidateRejection.None || traceCandidate is not null
                        ? Fit(family, indices, traceCandidate is not null) : default;

                    if (rejection == CandidateRejection.None && fit.Residual > MaximumResidual)
                    {
                        stages.RejectedStraightness++;
                        rejection = CandidateRejection.Straightness;
                    }
                    else if (rejection == CandidateRejection.None && (fit.Span < MinimumBaseSupport * scale ||
                        indices.Count / fit.Span < MinimumCoverage || fit.GapFraction > MaximumGapFraction || !fit.GapAligned))
                    {
                        stages.RejectedFragmentation++;
                        rejection = CandidateRejection.Fragmentation;
                    }
                    else if (rejection == CandidateRejection.None &&
                        Math.Abs(Fold(Math.Atan2(fit.Uy / yScale, fit.Ux / xScale) * 180 / Math.PI)) > Window)
                    {
                        stages.RejectedWindow++;
                        rejection = CandidateRejection.Window;
                    }

                    if (traceCandidate is not null)
                    {
                        var cell = BaseNormal(cosines[candidate.Theta], sines[candidate.Theta], candidate.Rho,
                            xScale, yScale);
                        var fitted = BaseNormal(-fit.Uy, fit.Ux, -fit.Uy * fit.X + fit.Ux * fit.Y, xScale, yScale);
                        traceCandidate(new(vertical, cell.Theta, cell.Rho, candidate.Votes, indices.Count / scale,
                            fit.Span / scale, fit.GapFraction, fit.Residual,
                            indices.Count > 1 ? fitted.Theta : null, indices.Count > 1 ? fitted.Rho : null, rejection,
                            BasePoint(fit, fit.Start, xScale, yScale), BasePoint(fit, fit.End, xScale, yScale)));
                    }

                    if (rejection != CandidateRejection.None) continue;

                    stages.FittedCandidates++;
                    // Rounded working dimensions have slightly different x/y scales.
                    // Transform directions geometrically, never with an empirical gain.
                    var fittedTilt = Fold(Math.Atan2(fit.Uy / yScale, fit.Ux / xScale) * 180 / Math.PI);
                    var radial = Math.Pow((fit.X - width * .5) / (width * .5), 2) +
                        Math.Pow((fit.Y - height * .5) / (height * .5), 2);
                    var length = Math.Min(fit.Span, indices.Count) / scale;
                    lines.Add(new(fittedTilt, vertical, length / (1 + radial)));

                    foreach (var i in indices)
                    {
                        used[i] = true;
                    }
                }
            }
        }

        return lines;
    }

    // Buckets one family's edges into thin tiles, so a candidate visits only the tiles its
    // support corridor crosses. The per-edge test is unchanged and the indices come back
    // ascending, so the gathered support is identical to a scan of the whole family.
    private sealed class EdgeGrid
    {
        private const int AlongTile = 64;

        private const int AcrossTile = 4;

        private readonly Edge[] _edges;

        private readonly bool _vertical;

        private readonly int _alongTiles;

        private readonly int _acrossTiles;

        private readonly int[] _starts;

        private readonly int[] _indices;

        internal EdgeGrid(Edge[] edges, bool vertical, int width, int height)
        {
            _edges = edges;
            _vertical = vertical;
            _alongTiles = (vertical ? height : width) / AlongTile + 1;
            _acrossTiles = (vertical ? width : height) / AcrossTile + 1;
            _starts = new int[_alongTiles * _acrossTiles + 1];

            foreach (var edge in edges)
            {
                _starts[Cell(edge) + 1]++;
            }

            for (var cell = 1; cell < _starts.Length; cell++)
            {
                _starts[cell] += _starts[cell - 1];
            }

            _indices = new int[edges.Length];
            var next = (int[])_starts.Clone();

            for (var i = 0; i < edges.Length; i++)
            {
                _indices[next[Cell(edges[i])]++] = i;
            }
        }

        internal List<int> Gather(double cosine, double sine, int rho, double tilt)
        {
            // The corridor |along * na + across * nc - rho| <= SupportDistance spans, per along
            // tile, the across range of the line at its two ends plus the corridor half-width.
            var na = _vertical ? sine : cosine;
            var nc = _vertical ? cosine : sine;
            var halfWidth = SupportDistance / Math.Abs(nc) + 1;
            var gathered = new List<int>();

            for (var tile = 0; tile < _alongTiles; tile++)
            {
                var first = (rho - tile * AlongTile * na) / nc;
                var last = (rho - (tile + 1) * AlongTile * na) / nc;
                var low = Tile(Math.Min(first, last) - halfWidth, AcrossTile, _acrossTiles);
                var high = Tile(Math.Max(first, last) + halfWidth, AcrossTile, _acrossTiles);

                for (var across = low; across <= high; across++)
                {
                    var cell = tile * _acrossTiles + across;

                    for (var k = _starts[cell]; k < _starts[cell + 1]; k++)
                    {
                        var i = _indices[k];
                        var edge = _edges[i];

                        if (Math.Abs(edge.Tilt - tilt) <= OrientationTolerance &&
                            Math.Abs(edge.X * cosine + edge.Y * sine - rho) <= SupportDistance)
                        {
                            gathered.Add(i);
                        }
                    }
                }
            }

            gathered.Sort();

            return gathered;
        }

        private int Cell(Edge edge) =>
            Tile(_vertical ? edge.Y : edge.X, AlongTile, _alongTiles) * _acrossTiles +
            Tile(_vertical ? edge.X : edge.Y, AcrossTile, _acrossTiles);

        private static int Tile(double value, int size, int count) =>
            Math.Clamp((int)Math.Floor(value / size), 0, count - 1);
    }

    private static GeometryPoint BasePoint(LineFit fit, double along, double xScale, double yScale) =>
        new((fit.X + along * fit.Ux + .5) / xScale - .5,
            (fit.Y + along * fit.Uy + .5) / yScale - .5);

    private static (double Theta, double Rho) BaseNormal(double nx, double ny, double rho,
        double xScale, double yScale)
    {
        // Working samples describe pixel centres, including the half-pixel resize offset.
        rho += .5 * (nx * (1 - xScale) + ny * (1 - yScale));
        nx *= xScale;
        ny *= yScale;
        var length = Math.Sqrt(nx * nx + ny * ny);

        return length > 0 ? (Math.Atan2(ny, nx) * 180 / Math.PI, rho / length) : default;
    }

    private static LineFit Fit(Edge[] edges, List<int> indices, bool trace)
    {
        if (indices.Count < 2) return default;

        var mx = 0d;
        var my = 0d;

        foreach (var i in indices)
        {
            mx += edges[i].X;
            my += edges[i].Y;
        }

        mx /= indices.Count;
        my /= indices.Count;
        var xx = 0d;
        var xy = 0d;
        var yy = 0d;

        foreach (var i in indices)
        {
            var dx = edges[i].X - mx;
            var dy = edges[i].Y - my;
            xx += dx * dx;
            xy += dx * dy;
            yy += dy * dy;
        }

        var direction = .5 * Math.Atan2(2 * xy, xx - yy);
        var ux = Math.Cos(direction);
        var uy = Math.Sin(direction);
        var residual = Math.Sqrt(Math.Max(0, (xx + yy - Math.Sqrt((xx - yy) * (xx - yy) + 4 * xy * xy)) /
            (2 * indices.Count)));
        if (!trace && residual > MaximumResidual) return new(mx, my, ux, uy, residual, 0, 0);

        var along = new double[indices.Count];

        for (var i = 0; i < indices.Count; i++)
        {
            var edge = edges[indices[i]];
            along[i] = (edge.X - mx) * ux + (edge.Y - my) * uy;
        }

        Array.Sort(along);
        var span = along[^1] - along[0];
        var gap = 0d;
        var split = 0d;

        for (var i = 1; i < along.Length; i++)
        {
            if (along[i] - along[i - 1] <= gap) continue;

            gap = along[i] - along[i - 1];
            split = (along[i] + along[i - 1]) * .5;
        }

        var gapFraction = span > 0 ? gap / span : 1;
        // Across a gap larger than a quarter of the span, independently
        // fitted fragments must follow the same direction. Otherwise two offset
        // parallel edges could masquerade as one slanted line with a small RMS.
        var aligned = gapFraction <= .25 || GapAligned(edges, indices, mx, my, ux, uy, split, direction);

        return new(mx, my, ux, uy, residual, span, gapFraction, aligned, along[0], along[^1]);
    }

    private static bool GapAligned(Edge[] edges, List<int> indices, double mx, double my,
        double ux, double uy, double split, double direction)
    {
        foreach (var before in new[] { true, false })
        {
            var fragment = indices.Where(i => ((edges[i].X - mx) * ux + (edges[i].Y - my) * uy < split) == before)
                .ToArray();
            if (fragment.Length < 2) return false;

            var x = fragment.Average(i => edges[i].X);
            var y = fragment.Average(i => edges[i].Y);
            var xx = 0d;
            var xy = 0d;
            var yy = 0d;

            foreach (var i in fragment)
            {
                var dx = edges[i].X - x;
                var dy = edges[i].Y - y;
                xx += dx * dx;
                xy += dx * dy;
                yy += dy * dy;
            }

            var angle = .5 * Math.Atan2(2 * xy, xx - yy);
            if (Math.Abs(Fold((angle - direction) * 180 / Math.PI)) > AgreementTolerance) return false;
        }

        return true;
    }
}
