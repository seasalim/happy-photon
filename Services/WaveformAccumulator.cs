using HappyPhoton.Models;

namespace HappyPhoton.Services;

public static class WaveformAccumulator
{
    public static WaveformData Accumulate(
        ReadOnlySpan<ushort> rgb,
        int width,
        int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (rgb.Length != checked(width * height * 3))
        {
            throw new ArgumentException(
                "The RGB span length must match the image dimensions.",
                nameof(rgb));
        }

        var waveform = new WaveformData();
        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * width * 3;
            for (var x = 0; x < width; x++)
            {
                var sourceOffset = rowOffset + x * 3;
                var r = rgb[sourceOffset] >> 8;
                var g = rgb[sourceOffset + 1] >> 8;
                var b = rgb[sourceOffset + 2] >> 8;
                var luminance = (int)(0.299 * r + 0.587 * g + 0.114 * b);
                luminance = Math.Clamp(luminance, 0, 255);

                var column = x * WaveformData.ColumnCount / width;
                var level = ToLevel(luminance);
                waveform.Luminance[level * WaveformData.ColumnCount + column]++;
                waveform.ColumnSampleCounts[column]++;
            }
        }

        if (width < WaveformData.ColumnCount)
        {
            BackFillColumns(waveform);
        }

        return waveform;
    }

    internal static int ToLevel(int value8) => value8 >> 1;

    private static void BackFillColumns(WaveformData waveform)
    {
        var sourceColumn = 0;
        for (var column = 1; column < WaveformData.ColumnCount; column++)
        {
            if (waveform.ColumnSampleCounts[column] != 0)
            {
                sourceColumn = column;
                continue;
            }

            waveform.ColumnSampleCounts[column] =
                waveform.ColumnSampleCounts[sourceColumn];
            for (var level = 0; level < WaveformData.LevelCount; level++)
            {
                waveform.Luminance[level * WaveformData.ColumnCount + column] =
                    waveform.Luminance[level * WaveformData.ColumnCount + sourceColumn];
            }
        }
    }
}
