using HappyPhoton.Models;
using System.Runtime.CompilerServices;

namespace HappyPhoton.Services;

internal static class HueWindow
{
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    internal static double Reliability(double chroma) => Smooth((chroma - .01) / .03);
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    internal static double Weight(HueRange range, double hue, double chroma)
    {
        if (!range.Enabled) return 1;
        if (range.Width == 0 && range.Softness == 0) return 0;
        var distance = Math.Abs(Math.IEEERemainder(hue - range.Center, 360));
        var window = distance <= range.Width / 2 ? 1 : range.Softness == 0 ? 0 :
            Smooth(1 - (distance - range.Width / 2) / range.Softness);
        return window * Reliability(chroma);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static double Smooth(double value)
    {
        var t = Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }
}
