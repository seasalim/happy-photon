using ImageMagick;

namespace HappyPhoton.Services;

internal static partial class RenderNoiseReduction
{
    private const int NativeScaleCount = 4;
    private const double MinimumEffectiveSupport = 0.3;
    private const float MaximumThreshold = 6200;
    private static readonly float[] WhiteNoiseFactors =
        [0.8907963f, 0.2006639f, 0.0855075f, 0.0412175f];

    internal static WaveletScale[] ResolveScales(
        MagickImage image,
        BaseImageInfo info,
        float amount)
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
        var result = new List<WaveletScale>(NativeScaleCount);
        for (var nativeIndex = 1; nativeIndex <= NativeScaleCount; nativeIndex++)
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
                ThresholdAt(exactIndex) * amount));
        }

        return result.ToArray();
    }

    private static float ThresholdAt(double exactIndex)
    {
        var boundedIndex = Math.Clamp(exactIndex, 1, NativeScaleCount);
        var lower = Math.Clamp(
            (int)Math.Floor(boundedIndex),
            1,
            NativeScaleCount - 1);
        var fraction = boundedIndex - lower;
        var first = Math.Log(WhiteNoiseFactors[lower - 1]);
        var second = Math.Log(WhiteNoiseFactors[lower]);
        return MaximumThreshold *
            (float)Math.Exp(first + (second - first) * fraction);
    }
}
