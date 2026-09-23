using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal static class LuminanceWindow
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining | System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    internal static double Weight(LuminanceRange range, double lightness)
    {
        if (!range.IsEffective) return 1;
        var (lower, upper, softness) = (range.Lower, range.Upper, range.Softness);
        if (lower == upper && softness == 0 && lower > 0 && upper < 1) return 0;
        var left = lower == 0 || lightness >= lower ? 1 : softness == 0 ? 0 : Smooth(1 + (lightness - lower) / softness);
        var right = upper == 1 || lightness <= upper ? 1 : softness == 0 ? 0 : Smooth(1 + (upper - lightness) / softness);
        return left * right;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static double Smooth(double value)
    {
        var t = Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }
}
