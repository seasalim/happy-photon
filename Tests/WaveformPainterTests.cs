using Avalonia.Media;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WaveformPainterTests
{
    [Fact]
    public void Intensity_IsSquareRootNormalizedPerColumn()
    {
        var waveform = new WaveformData();
        waveform.ColumnSampleCounts[7] = 200;
        waveform.Luminance[42 * WaveformData.ColumnCount + 7] = 1;

        var intensity = WaveformPainter.Intensity(waveform, 7, 42);

        Assert.Equal(0.5, intensity, precision: 12);
    }

    [Fact]
    public void Paint_WritesOpaquePremultipliedBgraWithThemeColors()
    {
        var waveform = new WaveformData();
        waveform.ColumnSampleCounts[7] = 200;
        waveform.Luminance[42 * WaveformData.ColumnCount + 7] = 1;
        var pixels = new byte[
            WaveformData.ColumnCount * WaveformData.LevelCount * 4];
        var backdrop = Color.FromRgb(10, 20, 30);
        var trace = Color.FromRgb(110, 120, 130);

        WaveformPainter.Paint(
            waveform,
            pixels,
            WaveformData.ColumnCount * 4,
            backdrop,
            trace);

        var y = WaveformData.LevelCount - 1 - 42;
        var offset = (y * WaveformData.ColumnCount + 7) * 4;
        Assert.Equal(80, pixels[offset]);
        Assert.Equal(70, pixels[offset + 1]);
        Assert.Equal(60, pixels[offset + 2]);
        Assert.Equal(255, pixels[offset + 3]);
        for (var index = 0; index < pixels.Length; index += 4)
        {
            Assert.Equal(255, pixels[index + 3]);
            Assert.True(pixels[index] <= pixels[index + 3]);
            Assert.True(pixels[index + 1] <= pixels[index + 3]);
            Assert.True(pixels[index + 2] <= pixels[index + 3]);
        }
    }

    [Fact]
    public void Paint_DoesNotAllocateAfterWarmup()
    {
        var waveform = new WaveformData();
        var pixels = new byte[
            WaveformData.ColumnCount * WaveformData.LevelCount * 4];
        var backdrop = Color.FromRgb(10, 20, 30);
        var trace = Color.FromRgb(110, 120, 130);
        WaveformPainter.Paint(
            waveform,
            pixels,
            WaveformData.ColumnCount * 4,
            backdrop,
            trace);

        var allocationStart = GC.GetAllocatedBytesForCurrentThread();
        WaveformPainter.Paint(
            waveform,
            pixels,
            WaveformData.ColumnCount * 4,
            backdrop,
            trace);

        Assert.Equal(
            allocationStart,
            GC.GetAllocatedBytesForCurrentThread());
    }
}
