using ImageMagick;

namespace HappyPhoton.Services;

internal static partial class RenderDetail
{
    internal static double CalculateEffectiveSigma(
        MagickImage image,
        BaseImageInfo info,
        double nativeSigma)
    {
        var nativeLongEdge = Math.Max(info.FullWidth, info.FullHeight);
        if (nativeSigma <= 0 || nativeLongEdge <= 0)
        {
            return 0;
        }

        var renderLongEdge = Math.Max(image.Width, image.Height);
        return nativeSigma * renderLongEdge / nativeLongEdge;
    }

    private static BoxBlurParameters CreateBoxBlur(double sigma)
    {
        var radius = Math.Max(
            1,
            (int)Math.Round((Math.Sqrt(1 + 12 * sigma * sigma) - 1) / 2));
        var variance = radius * (radius + 1) / 3.0;
        return new BoxBlurParameters(
            radius,
            (float)Math.Min(1, sigma * sigma / variance));
    }

    private readonly record struct BoxBlurParameters(
        int Radius,
        float Strength);
}
