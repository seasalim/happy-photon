using System.Runtime.CompilerServices;
using ImageMagick;

namespace HappyPhoton.Services;

internal static class RenderKernelSupport
{
    internal const float RedLuma = 0.2126f;
    internal const float GreenLuma = 0.7152f;
    internal const float BlueLuma = 0.0722f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float GetLuma(float red, float green, float blue) =>
        RedLuma * red + GreenLuma * green + BlueLuma * blue;

    internal static ushort ToQuantum(float value)
    {
        if (value <= ushort.MinValue) return ushort.MinValue;
        if (value >= ushort.MaxValue) return ushort.MaxValue;
        return (ushort)(value + 0.5f);
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
