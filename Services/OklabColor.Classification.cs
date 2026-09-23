using System.Runtime.CompilerServices;

namespace HappyPhoton.Services;

internal static partial class OklabColor
{
    internal readonly record struct Classification(double L, double A, double B)
    {
        internal double Chroma { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => Math.Sqrt(A * A + B * B); }
        internal double Hue { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => (Math.Atan2(B, A) * 180 / Math.PI + 360) % 360; }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    internal static Classification Classify(double r, double g, double b)
    {
        var l = Math.Cbrt(Rl * r + Gl * g + Bl * b);
        var m = Math.Cbrt(Rm * r + Gm * g + Bm * b);
        var s = Math.Cbrt(Rs * r + Gs * g + Bs * b);
        return new(Ll * l + Lm * m + Ls * s, Al * l + Am * m + As * s,
            Abl * l + Abm * m + Abs * s);
    }
}
