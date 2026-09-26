using HappyPhoton.Models;

namespace HappyPhoton.Tests;

internal enum HealFormulation { Membrane, Gaussian }
internal enum HealDomain { Additive, Ratio }
internal readonly record struct HealCandidate(HealFormulation Formulation, HealDomain Domain)
{
    public override string ToString() => $"{Formulation}-{Domain}";
    internal static IEnumerable<HealCandidate> All =>
        from f in Enum.GetValues<HealFormulation>() from d in Enum.GetValues<HealDomain>() select new HealCandidate(f, d);
}

// Stored radius is in long-edge units. Both kernels cap its pixel radius at half
// the short edge before clamping the source center; destination discs may cross edges.
internal readonly record struct HealSpot(double U, double V, double Su, double Sv,
    double Radius, bool IsClone = false, double Feather = .5, double Opacity = 1);

internal static class HealWorkloads
{
    internal const int Cap = 64;
    internal const double MinRadius = .002, MaxRadius = .10;
    private static double Q(double value) => Math.Round(value * 16384) / 16384;

    internal static HealSpot[] S64() => Enumerable.Range(0, Cap).Select(i =>
    {
        var u = .14 + (i % 8) * .095;
        var v = .18 + (i / 8) * .085;
        if (i % 8 == 1) u -= .065;
        return new HealSpot(Q(u), Q(v), Q(i % 4 == 0 ? .08 : .85 - (i % 7) * .08),
            Q(.10 + (i % 9) * .085), .005 + (i % 8) * .005, i % 4 == 3);
    }).ToArray();

    // Equal discs at this separation have half their area in common. A serpentine
    // grid spreads the workload across the frame; the two end columns may clip
    // by 0.004 long-edge units, which the destination-edge contract permits.
    internal static HealSpot[] SCap(int width, int height, double? areaLimit = null)
    {
        const double separationInRadii = .807945506599034;
        if (areaLimit is { } limit && (!double.IsFinite(limit) || limit < Area(S64())))
            throw new ArgumentOutOfRangeException(nameof(areaLimit), "The area limit must admit S64's total area.");
        // Compare areas directly, rather than floor a quotient that can round n discs
        // down to n-1. The runner computes count * this same per-disc area.
        var discArea = Math.PI * MaxRadius * MaxRadius;
        var count = areaLimit is { } area ? Enumerable.Range(1, Cap).Count(n => n * discArea <= area) : Cap;
        var columns = width >= height ? 11 : 6;
        var rows = (Cap + columns - 1) / columns;
        var step = separationInRadii * MaxRadius * Math.Max(width, height);
        return Enumerable.Range(0, count).Select(i =>
        {
            var row = i / columns;
            var column = row % 2 == 0 ? i % columns : columns - 1 - i % columns;
            return new HealSpot((width / 2d + (column - (columns - 1) / 2d) * step) / width,
                (height / 2d + (row - (rows - 1) / 2d) * step) / height, .2, .25, MaxRadius);
        }).ToArray();
    }

    internal static double Area(IEnumerable<HealSpot> spots) => spots.Sum(s => Math.PI * s.Radius * s.Radius);

    // Use the already-qualified production locals control without a second definition.
    internal static EditSettings LH8() => LocalsBrushWorkloads.Settings(true);
}
