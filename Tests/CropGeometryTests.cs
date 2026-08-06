using HappyPhoton.Models;
using Xunit;

namespace HappyPhoton.Tests;

public class CropGeometryTests
{
    [Theory]
    [InlineData(0, 300, 3.0)]
    [InlineData(400, 0, 3.0)]
    [InlineData(400, 300, 0.0)]
    public void SafeBoundsAfterRotation_ReturnsNullWhenNotApplicable(double width, double height, double degrees)
    {
        Assert.Null(CropGeometry.SafeBoundsAfterRotation(width, height, degrees));
    }

    [Theory]
    [InlineData(400, 300, 0.5)]
    [InlineData(400, 300, 3.0)]
    [InlineData(400, 300, 15.0)]
    [InlineData(400, 300, 44.0)]
    [InlineData(400, 300, -3.0)]
    [InlineData(300, 400, 7.5)]
    [InlineData(511, 293, 30.0)]
    public void SafeBoundsAfterRotation_IsCenteredAndWithinRange(double width, double height, double degrees)
    {
        var bounds = CropGeometry.SafeBoundsAfterRotation(width, height, degrees);

        Assert.NotNull(bounds);
        Assert.InRange(bounds.Left, 0, 0.5);
        Assert.InRange(bounds.Top, 0, 0.5);
        Assert.InRange(bounds.Right, 0.5, 1);
        Assert.InRange(bounds.Bottom, 0.5, 1);
        Assert.Equal(1.0, bounds.Left + bounds.Right, 6);
        Assert.Equal(1.0, bounds.Top + bounds.Bottom, 6);
    }

    [Theory]
    [InlineData(400, 300, 0.5)]
    [InlineData(400, 300, 3.0)]
    [InlineData(400, 300, 15.0)]
    [InlineData(400, 300, 44.0)]
    [InlineData(400, 300, -3.0)]
    [InlineData(300, 400, 7.5)]
    [InlineData(511, 293, 30.0)]
    public void SafeBoundsAfterRotation_CornersLieInsideRotatedImage(double width, double height, double degrees)
    {
        var angle = degrees * Math.PI / 180.0;
        var rotatedWidth = width * Math.Abs(Math.Cos(angle)) + height * Math.Abs(Math.Sin(angle));
        var rotatedHeight = width * Math.Abs(Math.Sin(angle)) + height * Math.Abs(Math.Cos(angle));

        var bounds = CropGeometry.SafeBoundsAfterRotation(width, height, degrees, rotatedWidth, rotatedHeight);
        Assert.NotNull(bounds);

        foreach (var (fx, fy) in new[]
        {
            (bounds.Left, bounds.Top),
            (bounds.Right, bounds.Top),
            (bounds.Left, bounds.Bottom),
            (bounds.Right, bounds.Bottom)
        })
        {
            // Map the corner back into the unrotated image frame; it must land inside.
            var px = fx * rotatedWidth - rotatedWidth / 2;
            var py = fy * rotatedHeight - rotatedHeight / 2;
            var sourceX = px * Math.Cos(angle) + py * Math.Sin(angle);
            var sourceY = -px * Math.Sin(angle) + py * Math.Cos(angle);

            Assert.InRange(Math.Abs(sourceX), 0, width / 2);
            Assert.InRange(Math.Abs(sourceY), 0, height / 2);
        }
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
