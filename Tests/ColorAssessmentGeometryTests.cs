using Avalonia;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ColorAssessmentGeometryTests
{
    [Theory]
    [InlineData(800, 600, 24, 704, 504)]
    [InlineData(1000, 800, 32, 872, 672)]
    [InlineData(1600, 1200, 48, 1408, 1008)]
    [InlineData(4000, 3000, 48, 3808, 2808)]
    public void AssessmentField_UsesClampedBandAndReservedFitBox(
        double width,
        double height,
        double band,
        double fitWidth,
        double fitHeight)
    {
        var geometry = ColorAssessmentGeometry.Calculate(
            new Size(width, height),
            isColorAssessment: true);

        Assert.True(geometry.IsFieldVisible);
        Assert.Equal(band, geometry.BandWidth);
        Assert.Equal(new Size(fitWidth, fitHeight), geometry.FitBox);
        Assert.True(width - fitWidth >= 4 * band);
        Assert.True(height - fitHeight >= 4 * band);
    }

    [Theory]
    [InlineData(0, 600)]
    [InlineData(800, 0)]
    [InlineData(-1, 600)]
    [InlineData(800, double.NaN)]
    [InlineData(95, 600)]
    public void DegenerateViewport_SuppressesAssessmentField(
        double width,
        double height)
    {
        var geometry = ColorAssessmentGeometry.Calculate(
            new Size(width, height),
            isColorAssessment: true);

        Assert.False(geometry.IsFieldVisible);
        Assert.Equal(0, geometry.BandWidth);
    }

    [Theory]
    [InlineData(800, 600)]
    [InlineData(1279.5, 719.5)]
    public void ModeOff_FitBoxIsBitIdenticalToViewport(
        double width,
        double height)
    {
        var viewport = new Size(width, height);

        var geometry = ColorAssessmentGeometry.Calculate(
            viewport,
            isColorAssessment: false);

        Assert.False(geometry.IsFieldVisible);
        Assert.Equal(0, geometry.BandWidth);
        Assert.Equal(viewport, geometry.FitBox);
    }
}
