using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RenderGeometryTests
{
    public static TheoryData<EditSettings> Cases => new()
    {
        new EditSettings { Rotation = 90 },
        new EditSettings { HorizonRotation = 7.5 },
        new EditSettings
        {
            Crop = new CropRegion
            {
                Left = 0.1,
                Top = 0.2,
                Right = 0.8,
                Bottom = 0.9
            }
        },
        new EditSettings
        {
            Rotation = 270,
            HorizonRotation = -4,
            Crop = new CropRegion
            {
                Left = 0.15,
                Top = 0.1,
                Right = 0.9,
                Bottom = 0.8
            }
        }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Apply_MatchesReferenceGeometry(EditSettings settings)
    {
        using var expected = new MagickImage(MagickColors.Red, 81, 53);
        using var actual = (MagickImage)expected.Clone();

        GeometryReferenceRenderer.Apply(expected, settings);
        RenderGeometry.Apply(actual, settings);

        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(ReadPixels(expected), ReadPixels(actual));
    }

    private static ushort[] ReadPixels(MagickImage image) =>
        image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGB) ??
        throw new InvalidOperationException("Unable to read RGB pixels.");
}
