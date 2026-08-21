using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WaveformAccumulatorTests
{
    [Fact]
    public void Data_UsesCanonicalGridDimensions()
    {
        var data = new WaveformData();

        Assert.Equal(256, WaveformData.ColumnCount);
        Assert.Equal(128, WaveformData.LevelCount);
        Assert.Equal(256 * 128, data.Luminance.Length);
        Assert.Equal(256, data.ColumnSampleCounts.Length);
    }

    [Fact]
    public void Accumulate_PlacesSamplesInExactColumnsAndLevels()
    {
        var rgb = new ushort[WaveformData.ColumnCount * 3];
        SetGray(rgb, 0, 0);
        SetRgb(rgb, 1, 0, 4, 0);
        SetRgb(rgb, 127, 0, 219, 0);
        SetGray(rgb, 255, 255);

        var data = WaveformAccumulator.Accumulate(
            rgb,
            WaveformData.ColumnCount,
            1);

        Assert.Equal((ushort)1, Cell(data, 0, 0));
        Assert.Equal((ushort)1, Cell(data, 1, 1));
        Assert.Equal((ushort)1, Cell(data, 127, 64));
        Assert.Equal((ushort)1, Cell(data, 255, 127));
        Assert.All(data.ColumnSampleCounts, count => Assert.Equal(1, count));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(254, 127)]
    [InlineData(255, 127)]
    public void Accumulate_UsesHighByteThenShiftBoundary(
        byte value,
        int expectedLevel)
    {
        Assert.Equal(expectedLevel, WaveformAccumulator.ToLevel(value));
    }

    [Theory]
    [InlineData(255, 0, 0)]
    [InlineData(0, 255, 0)]
    [InlineData(0, 0, 255)]
    [InlineData(18, 52, 86)]
    [InlineData(255, 255, 255)]
    public void Accumulate_MatchesHistogramRec601(
        byte red,
        byte green,
        byte blue)
    {
        ushort[] rgb = [(ushort)(red << 8), (ushort)(green << 8), (ushort)(blue << 8)];
        var histogramLuminance = Math.Clamp(
            (int)(0.299 * red + 0.587 * green + 0.114 * blue),
            0,
            255);

        var data = WaveformAccumulator.Accumulate(rgb, 1, 1);

        Assert.Equal((ushort)1, Cell(data, 0, histogramLuminance >> 1));
    }

    [Fact]
    public void Accumulate_BackFillsNarrowSourcesAcrossEveryColumn()
    {
        ushort[] rgb =
        [
            0, 0, 0,
            ushort.MaxValue, ushort.MaxValue, ushort.MaxValue,
            0, 0, 0,
            ushort.MaxValue, ushort.MaxValue, ushort.MaxValue
        ];

        var data = WaveformAccumulator.Accumulate(rgb, width: 2, height: 2);

        for (var column = 0; column < WaveformData.ColumnCount; column++)
        {
            var expectedLevel = column < 128 ? 0 : 127;
            Assert.Equal((ushort)2, data.ColumnSampleCounts[column]);
            Assert.Equal((ushort)2, Cell(data, column, expectedLevel));
        }
    }

    [Fact]
    public void ProductionDimensionCannotOverflowUshortCells()
    {
        var sourceColumnsPerCell =
            (BaseImage.InteractivePreviewMaxDimension +
             WaveformData.ColumnCount - 1) /
            WaveformData.ColumnCount;
        var maximumCellCount = sourceColumnsPerCell *
            BaseImage.InteractivePreviewMaxDimension;

        Assert.Equal(11200, maximumCellCount);
        Assert.True(maximumCellCount <= ushort.MaxValue);
    }

    private static ushort Cell(WaveformData data, int column, int level) =>
        data.Luminance[level * WaveformData.ColumnCount + column];

    private static void SetGray(ushort[] rgb, int pixel, byte value)
        => SetRgb(rgb, pixel, value, value, value);

    private static void SetRgb(
        ushort[] rgb,
        int pixel,
        byte red,
        byte green,
        byte blue)
    {
        var offset = pixel * 3;
        rgb[offset] = (ushort)(red << 8);
        rgb[offset + 1] = (ushort)(green << 8);
        rgb[offset + 2] = (ushort)(blue << 8);
    }
}
