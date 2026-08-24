using HappyPhoton.Models;
using Xunit;

namespace HappyPhoton.Tests;

public class CropGeometryTests
{
    [Theory]
    [InlineData(0, 300, 400, 300)]
    [InlineData(400, -1, 400, 300)]
    [InlineData(400, 300, 0, 300)]
    [InlineData(400, 300, 400, -1)]
    public void RelativeAspectRatioDifference_ReturnsNullForInvalidDimensions(
        long referenceWidth,
        long referenceHeight,
        long candidateWidth,
        long candidateHeight)
    {
        Assert.Null(CropGeometry.RelativeAspectRatioDifference(
            referenceWidth,
            referenceHeight,
            candidateWidth,
            candidateHeight));
    }

    [Fact]
    public void RelativeAspectRatioDifference_UsesReferenceRatioDenominator()
    {
        var sourceReference = CropGeometry.RelativeAspectRatioDifference(
            6000, 4000, 400, 300);
        var previewReference = CropGeometry.RelativeAspectRatioDifference(
            400, 300, 6000, 4000);

        Assert.NotNull(sourceReference);
        Assert.NotNull(previewReference);
        Assert.Equal((1.5 - 4.0 / 3.0) / 1.5, sourceReference.Value, 12);
        Assert.Equal(
            (1.5 - 4.0 / 3.0) / (4.0 / 3.0),
            previewReference.Value,
            12);
    }

    [Theory]
    [InlineData(120, 80, 120, 68, 0, 6, 120, 68)]
    [InlineData(80, 120, 68, 120, 6, 0, 68, 120)]
    [InlineData(101, 80, 3, 2, 0, 6, 101, 67)]
    [InlineData(2, 100, 1000, 1, 0, 0, 1, 100)]
    public void CenterCropToAspect_PreservesOrientationRoundingAndClamping(
        long cropWidth,
        long cropHeight,
        long referenceWidth,
        long referenceHeight,
        int expectedX,
        int expectedY,
        uint expectedWidth,
        uint expectedHeight)
    {
        var crop = CropGeometry.CenterCropToAspect(
            cropWidth,
            cropHeight,
            referenceWidth,
            referenceHeight);

        Assert.Equal(
            new CenterCropRectangle(
                expectedX,
                expectedY,
                expectedWidth,
                expectedHeight),
            crop);
    }

    [Fact]
    public void Intersect_CropInsideBoundsIsUnchanged()
    {
        var crop = new CropRegion { Left = 0.2, Top = 0.2, Right = 0.8, Bottom = 0.8 };
        var bounds = new CropRegion { Left = 0.1, Top = 0.1, Right = 0.9, Bottom = 0.9 };

        var result = CropGeometry.Intersect(crop, bounds);

        Assert.Equal(0.2, result.Left);
        Assert.Equal(0.2, result.Top);
        Assert.Equal(0.8, result.Right);
        Assert.Equal(0.8, result.Bottom);
    }

    [Fact]
    public void Intersect_CropOutsideBoundsIsClamped()
    {
        var crop = new CropRegion { Left = 0.0, Top = 0.0, Right = 1.0, Bottom = 1.0 };
        var bounds = new CropRegion { Left = 0.1, Top = 0.15, Right = 0.9, Bottom = 0.85 };

        var result = CropGeometry.Intersect(crop, bounds);

        Assert.Equal(0.1, result.Left);
        Assert.Equal(0.15, result.Top);
        Assert.Equal(0.9, result.Right);
        Assert.Equal(0.85, result.Bottom);
    }

    [Fact]
    public void Intersect_DisjointCropYieldsValidRegionWithinBounds()
    {
        var crop = new CropRegion { Left = 0.0, Top = 0.0, Right = 0.05, Bottom = 0.05 };
        var bounds = new CropRegion { Left = 0.3, Top = 0.3, Right = 0.7, Bottom = 0.7 };

        var result = CropGeometry.Intersect(crop, bounds);

        Assert.True(result.Left < result.Right);
        Assert.True(result.Top < result.Bottom);
        Assert.InRange(result.Left, bounds.Left, bounds.Right);
        Assert.InRange(result.Right, bounds.Left, bounds.Right);
        Assert.InRange(result.Top, bounds.Top, bounds.Bottom);
        Assert.InRange(result.Bottom, bounds.Top, bounds.Bottom);
    }
}
