using Avalonia.Media;
using HappyPhoton.Models;

namespace HappyPhoton.Views;

public static class WaveformPainter
{
    // A cell containing 2% of its column's samples reaches full trace intensity.
    internal const double ReferenceFraction = 0.02;

    public static void Paint(
        WaveformData? waveform,
        Span<byte> destination,
        int rowBytes,
        Color backdrop,
        Color trace)
    {
        var requiredLength = checked(rowBytes * WaveformData.LevelCount);
        if (rowBytes < WaveformData.ColumnCount * 4 ||
            destination.Length < requiredLength)
        {
            throw new ArgumentException(
                "The destination must hold a 256 by 128 BGRA image.",
                nameof(destination));
        }

        for (var y = 0; y < WaveformData.LevelCount; y++)
        {
            var level = WaveformData.LevelCount - 1 - y;
            var rowOffset = y * rowBytes;
            for (var column = 0; column < WaveformData.ColumnCount; column++)
            {
                var intensity = Intensity(waveform, column, level);
                var offset = rowOffset + column * 4;
                destination[offset] = Lerp(backdrop.B, trace.B, intensity);
                destination[offset + 1] = Lerp(backdrop.G, trace.G, intensity);
                destination[offset + 2] = Lerp(backdrop.R, trace.R, intensity);
                destination[offset + 3] = byte.MaxValue;
            }
        }
    }

    internal static double Intensity(
        WaveformData? waveform,
        int column,
        int level)
    {
        if (waveform == null)
        {
            return 0;
        }

        var sampleCount = waveform.ColumnSampleCounts[column];
        if (sampleCount == 0)
        {
            return 0;
        }

        var count = waveform.Luminance[
            level * WaveformData.ColumnCount + column];
        return Math.Min(
            1,
            Math.Sqrt(count / (sampleCount * ReferenceFraction)));
    }

    private static byte Lerp(byte from, byte to, double amount) =>
        (byte)Math.Round(from + (to - from) * amount);
}
