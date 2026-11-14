using HappyPhoton.Models;
using Xunit;

namespace HappyPhoton.Tests;

public partial class CropGeometryTests
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
}
