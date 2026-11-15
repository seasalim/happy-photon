using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class OrientationEnumTests
{
    [Theory]
    [InlineData(Orientation.TopLeft, 1)]
    [InlineData(Orientation.TopRight, 2)]
    [InlineData(Orientation.BottomRight, 3)]
    [InlineData(Orientation.BottomLeft, 4)]
    [InlineData(Orientation.LeftTop, 5)]
    [InlineData(Orientation.RightTop, 6)]
    [InlineData(Orientation.RightBottom, 7)]
    [InlineData(Orientation.LeftBottom, 8)]
    public void NumericValueMatchesExif(Orientation orientation, int exifValue) =>
        Assert.Equal(exifValue, (int)orientation);
}
