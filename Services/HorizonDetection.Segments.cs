namespace HappyPhoton.Services;

public static partial class HorizonDetection
{
    // Small gaps tolerate interrupted Canny support; larger occlusions require
    // independently straight, collinear runs, so sparse texture cannot bridge them.
    private const double MaximumBaseLineGap = 64;

    // Half the minimum line support establishes a fragment's direction. Two such
    // fragments can recover a 600px edge with its middle 60% hidden.
    private const double MinimumBaseFragmentSupport = MinimumBaseSupport / 2;

    // One label-length occlusion is allowed only between independently fitted runs.
    // The derivative footprint is added for lost pixels at softened endpoints.
    private const double MaximumBaseOcclusion = 400;

    private static List<List<int>> Segments(Edge[] edges, List<int> indices,
        double ux, double uy, double scale)
    {
        var ordered = indices.ToArray();
        var positions = new double[ordered.Length];

        for (var i = 0; i < ordered.Length; i++)
        {
            var edge = edges[ordered[i]];
            positions[i] = edge.X * ux + edge.Y * uy;
        }

        Array.Sort(positions, ordered);

        // Gathered indices are ascending. Restore that order for equal projections,
        // matching OrderBy's stable ties and therefore each later fit's accumulation.
        for (var first = 0; first < ordered.Length;)
        {
            var end = first + 1;

            while (end < ordered.Length && positions[end] == positions[first])
            {
                end++;
            }

            if (end - first > 1) Array.Sort(ordered, first, end - first);
            first = end;
        }

        var runs = new List<List<int>>();

        foreach (var i in ordered)
        {
            var previous = runs.Count > 0 ? runs[^1][^1] : i;
            var gap = (edges[i].X - edges[previous].X) * ux + (edges[i].Y - edges[previous].Y) * uy;

            if (runs.Count == 0 || gap > MaximumBaseLineGap * scale)
            {
                runs.Add([]);
            }

            runs[^1].Add(i);
        }

        foreach (var run in runs)
        {
            TrimLooseEnds(edges, run, ux, uy);
        }

        // A single run has nothing to join; the caller already fits it for acceptance.
        if (runs.Count <= 1) return runs.Count > 0 ? runs : [[]];

        // Keep rejected fragments for diagnostics, but never let them join a line.
        for (var first = 0; first < runs.Count; first++)
        {
            if (runs[first].Count < MinimumBaseFragmentSupport * scale) continue;

            var fit = Fit(edges, runs[first], false);
            if (!RigidFragment(runs[first], fit, scale)) continue;

            for (var next = first + 1; next < runs.Count; next++)
            {
                if (runs[next].Count < MinimumBaseFragmentSupport * scale) continue;

                var other = Fit(edges, runs[next], false);
                if (!RigidFragment(runs[next], other, scale)) continue;
                if (!Collinear(fit, other, scale)) continue;

                var combined = runs[first].Concat(runs[next]).ToList();
                var merged = Fit(edges, combined, false);
                if (merged.Residual > MaximumResidual || !merged.GapAligned) continue;

                runs[first] = combined;
                runs.RemoveAt(next--);
                fit = merged;
            }
        }

        return runs.Count > 0 ? runs : [[]];
    }

    private static void TrimLooseEnds(Edge[] edges, List<int> run, double ux, double uy)
    {
        // Detached tails shorter than the derivative footprint cannot establish
        // a direction. Exclude them before they extend a real edge's span.
        var start = 0;
        var end = run.Count;

        for (var i = 1; i < run.Count; i++)
        {
            var gap = (edges[run[i]].X - edges[run[i - 1]].X) * ux +
                (edges[run[i]].Y - edges[run[i - 1]].Y) * uy;
            if (gap <= 2 * DerivativeRadius) continue;
            if (i < 2 * DerivativeRadius) start = i;
            if (run.Count - i < 2 * DerivativeRadius) end = Math.Min(end, i);
        }

        if (end < run.Count) run.RemoveRange(end, run.Count - end);
        if (start > 0) run.RemoveRange(0, Math.Min(start, run.Count));
    }

    private static bool RigidFragment(List<int> indices, LineFit fit, double scale) =>
        indices.Count >= MinimumBaseFragmentSupport * scale && fit.Residual <= MaximumResidual &&
        fit.Span > 0 && indices.Count / fit.Span >= MinimumCoverage && fit.GapAligned;

    private static bool Collinear(LineFit first, LineFit second, double scale)
    {
        var angle = Math.Atan2(first.Uy, first.Ux) - Math.Atan2(second.Uy, second.Ux);
        if (Math.Abs(Fold(angle * 180 / Math.PI)) > AgreementTolerance) return false;

        var dx = second.X - first.X;
        var dy = second.Y - first.Y;
        var along = dx * first.Ux + dy * first.Uy;
        var alignment = first.Ux * second.Ux + first.Uy * second.Uy;
        var start = along + second.Start * alignment;
        var end = along + second.End * alignment;
        var gap = Math.Max(Math.Min(start, end) - first.End, first.Start - Math.Max(start, end));
        if (gap > MaximumBaseOcclusion * scale + 2 * DerivativeRadius) return false;

        // Check each run against the other's normal: parallel offsets and nearly
        // crossing fragments must not become a new slanted line across the gap.
        return Math.Abs(dx * first.Uy - dy * first.Ux) <= SupportDistance &&
            Math.Abs(dx * second.Uy - dy * second.Ux) <= SupportDistance;
    }
}
