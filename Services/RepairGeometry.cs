using HappyPhoton.Models;

namespace HappyPhoton.Services;

/// <summary>Aspect-dependent repair geometry, evaluated only when the base is known.</summary>
public static class RepairGeometry
{
    public static double EffectiveRadius(double radius, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (!double.IsFinite(radius) || radius < 0)
            throw new ArgumentOutOfRangeException(nameof(radius));
        return Math.Min(radius * Math.Max(width, height), Math.Min(width, height) / 2d);
    }

    public static (double U, double V) ClampSource(Repair repair, int width, int height)
    {
        var radius = EffectiveRadius(repair.Radius, width, height);
        return (Math.Clamp(repair.Su, radius / width, 1 - radius / width),
            Math.Clamp(repair.Sv, radius / height, 1 - radius / height));
    }
}
