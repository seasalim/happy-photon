using ImageMagick;

namespace HappyPhoton.Services;

internal static partial class RenderNoiseReduction
{
    private const double MinimumEffectiveSupport = 0.3;
    private const float MaximumLumaThreshold = 6200;
    private const float MaximumChromaThreshold = 6_500;
    private static readonly float[] LumaNoiseFactors =
        [0.8907963f, 0.2006639f, 0.0855075f, 0.0412175f];
    private static readonly float[] ChromaNoiseFactors =
        [0.90f, 0.25f, 0.12f, 0.08f, 0.05f];

    internal static WaveletScale[] ResolveScales(
        MagickImage image,
        BaseImageInfo info,
        float amount) =>
        ResolveScales(
            image,
            info,
            amount,
            MaximumLumaThreshold,
            LumaNoiseFactors);

    internal static WaveletScale[] ResolveChromaScales(
        MagickImage image,
        BaseImageInfo info,
        float amount) =>
        ResolveScales(
            image,
            info,
            amount,
            MaximumChromaThreshold,
            ChromaNoiseFactors);

    private static WaveletScale[] ResolveScales(
        MagickImage image,
        BaseImageInfo info,
        float amount,
        float maximumThreshold,
        float[] noiseFactors)
    {
        var nativeLongEdge = Math.Max(info.FullWidth, info.FullHeight);
        if (nativeLongEdge <= 0 || amount <= 0)
        {
            return [];
        }

        var renderLongEdge = Math.Max(image.Width, image.Height);
        var renderShortEdge = Math.Min(image.Width, image.Height);
        var indexOffset = Math.Log2(
            renderLongEdge / (double)nativeLongEdge);
        var result = new List<WaveletScale>(noiseFactors.Length);
        for (var nativeIndex = 1;
             nativeIndex <= noiseFactors.Length;
             nativeIndex++)
        {
            var exactIndex = nativeIndex + indexOffset;
            if (Math.Pow(2, exactIndex) < MinimumEffectiveSupport)
            {
                continue;
            }
            var quantizedIndex = (int)Math.Round(
                exactIndex,
                MidpointRounding.AwayFromZero);
            if (quantizedIndex < 1 || quantizedIndex > 30)
            {
                continue;
            }

            var dilation = 1 << (quantizedIndex - 1);
            var supportRadius = checked(dilation * 2);
            if (supportRadius > renderShortEdge / 4.0)
            {
                continue;
            }

            result.Add(new WaveletScale(
                dilation,
                ThresholdAt(
                    exactIndex,
                    maximumThreshold,
                    noiseFactors) * amount));
        }

        return result.ToArray();
    }

    private static float ThresholdAt(
        double exactIndex,
        float maximumThreshold,
        float[] noiseFactors)
    {
        var boundedIndex = Math.Clamp(exactIndex, 1, noiseFactors.Length);
        var lower = Math.Clamp(
            (int)Math.Floor(boundedIndex),
            1,
            noiseFactors.Length - 1);
        var fraction = boundedIndex - lower;
        var first = Math.Log(noiseFactors[lower - 1]);
        var second = Math.Log(noiseFactors[lower]);
        return maximumThreshold *
            (float)Math.Exp(first + (second - first) * fraction);
    }

    internal readonly record struct WaveletScale(
        int Dilation,
        float Threshold)
    {
        internal int SupportRadius => checked(Dilation * 2);
    }
}
