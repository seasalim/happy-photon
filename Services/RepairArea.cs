using HappyPhoton.Models;

namespace HappyPhoton.Services;

/// <summary>Shared stored-disc area accounting for persistence and repair editing.</summary>
public static class RepairArea
{
    // Covers floating-point rounding only, not a perceptible increase in disc size.
    public const double RelativeTolerance = 1e-12;

    /// <summary>Neumaier sum over finite radii clamped to the stored range; overlaps count.</summary>
    public static double Sum(IEnumerable<Repair> repairs)
    {
        double sum = 0, compensation = 0;
        foreach (var repair in repairs)
        {
            var radius = Math.Clamp(repair.Radius, Repair.MinimumRadius, Repair.MaximumRadius);
            var area = Math.PI * radius * radius;
            var next = sum + area;
            compensation += Math.Abs(sum) >= Math.Abs(area)
                ? (sum - next) + area : (area - next) + sum;
            sum = next;
        }
        return sum + compensation;
    }
}
