using System.Runtime.CompilerServices;
using ImageMagick;

namespace HappyPhoton.Services;

internal static class RenderKernelSupport
{
    internal const int DefaultBandPixelLimit = 8_000_000;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float GetLuma(float red, float green, float blue) =>
        (float)(Rec2020Luminance.Red * red +
            Rec2020Luminance.Green * green +
            Rec2020Luminance.Blue * blue);

    internal static ushort ToQuantum(float value)
    {
        if (value <= ushort.MinValue) return ushort.MinValue;
        if (value >= ushort.MaxValue) return ushort.MaxValue;
        return (ushort)(value + 0.5f);
    }

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

    internal static PixelLayout GetLayout(
        IPixelCollection<ushort> pixels) =>
        new(
            checked((int)pixels.Channels),
            GetChannelIndex(pixels, PixelChannel.Red),
            GetChannelIndex(pixels, PixelChannel.Green),
            GetChannelIndex(pixels, PixelChannel.Blue));

    private static int GetChannelIndex(
        IPixelCollection<ushort> pixels,
        PixelChannel channel) =>
        checked((int)(pixels.GetChannelIndex(channel) ??
            throw new InvalidOperationException(
                $"The image has no {channel} channel.")));

    internal readonly record struct PixelLayout(
        int Channels,
        int Red,
        int Green,
        int Blue);
}
