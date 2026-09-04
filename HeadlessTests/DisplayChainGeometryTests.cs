using Avalonia;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DisplayChainGeometryTests
{
    [Fact]
    public void Calculator_AccountsForNonUnitRenderScaling()
    {
        var mapping = DisplayChainMappingCalculator.Calculate(
            new PixelSize(800, 400),
            new Size(400, 200),
            new Size(600, 500),
            1.5);

        Assert.Equal(new Rect(0, 0, 600, 300), mapping.DeviceRectangle);
        Assert.Equal(0.75, mapping.NetScaleX);
        Assert.Equal(0.75, mapping.NetScaleY);
        Assert.False(mapping.IsOneToOne);
    }

    [Fact]
    public void Calculator_TreatsSubHalfPercentRoundingAsOneToOne()
    {
        var mapping = DisplayChainMappingCalculator.Calculate(
            new PixelSize(1000, 1000),
            new Size(995, 1005),
            new Size(1000, 1000),
            1);

        Assert.True(mapping.IsOneToOne);
    }

    [Fact]
    public void FitZoomLevel_NeverEnlargesPastOneToOne()
    {
        var small = new PixelSize(256, 256);
        var fitBox = new Size(1900, 1200);

        Assert.Equal(1, ZoomGeometryCalculator.FitZoomLevel(small, fitBox, 1));
        Assert.Equal(1, ZoomGeometryCalculator.FitZoomLevel(small, fitBox, 1.5));
        Assert.Equal(256, ZoomGeometryCalculator.FittedDeviceLongEdge(small, fitBox, 1.5));
        Assert.Equal(0.6, ZoomGeometryCalculator.FitZoomLevel(
            new PixelSize(2000, 2000), fitBox, 1), 10);
    }
}
