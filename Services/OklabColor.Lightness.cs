namespace HappyPhoton.Services;

internal static partial class OklabColor
{
    // Unclamped pre-tone classification; no chroma or hue is needed for luminance ranges.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static double ClassifyLightness(double r, double g, double b) =>
        Ll * Math.Cbrt(Rl * r + Gl * g + Bl * b) +
        Lm * Math.Cbrt(Rm * r + Gm * g + Bm * b) +
        Ls * Math.Cbrt(Rs * r + Gs * g + Bs * b);
}
