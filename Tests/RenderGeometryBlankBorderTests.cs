using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RenderGeometryBlankBorderTests
{
    public static TheoryData<int, int, GeometrySettings, double> Sweep
    {
        get
        {
            var geometries = new List<GeometrySettings>();
            foreach (var value in new[] { -100, -50, 50, 100 })
            {
                geometries.Add(new GeometrySettings { Vertical = value });
                geometries.Add(new GeometrySettings { Horizontal = value });
                geometries.Add(new GeometrySettings { Aspect = value });
                geometries.Add(new GeometrySettings { Distortion = value });
            }
            geometries.Add(new GeometrySettings
            {
                Vertical = 60,
                Horizontal = -45,
                Aspect = 35,
                Distortion = -70
            });
            geometries.Add(new GeometrySettings
            {
                Vertical = -100,
                Horizontal = 75,
                Aspect = -80,
                Distortion = 100
            });
            geometries.Add(new GeometrySettings
            {
                Vertical = 90,
                Horizontal = 90,
                Aspect = 100,
                Distortion = -100
            });

            var data = new TheoryData<int, int, GeometrySettings, double>();
            var horizons = new[] { 0d, -12d, -3d, 3d, 12d };
            for (var index = 0; index < geometries.Count; index++)
            {
                // Every control boundary and interaction case runs on both an
                // even and odd shape; horizon boundaries are distributed
                // across the odd-shape cases instead of cross-multiplied.
                data.Add(400, 300, geometries[index], 0);
                data.Add(511, 293, geometries[index], horizons[index % horizons.Length]);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Sweep))]
    public void ActiveGeometry_LeavesNoBlankBorderPixels(
        int width,
        int height,
        GeometrySettings geometry,
        double horizon)
    {
        using var source = CreateRedImage((uint)width, (uint)height);
        var map = new RenderGeometryMap(width, height, horizon, geometry);

        AssertBoundaryIsCovered(map);

        using var corrected = RenderGeometry.Apply(
            source,
            new EditSettings
            {
                HorizonRotation = horizon,
                Geometry = geometry
            },
            out _);

        Assert.Equal(0, CountBlankBorderPixels(corrected));
    }

    private static void AssertBoundaryIsCovered(RenderGeometryMap map)
    {
        var maxX = map.OutputWidth - 1;
        var maxY = map.OutputHeight - 1;
        for (var x = 0; x < map.OutputWidth; x++)
        {
            AssertCovered(map, map.MapInverse(x, 0));
            AssertCovered(map, map.MapInverse(x, maxY));
        }
        for (var y = 1; y < maxY; y++)
        {
            AssertCovered(map, map.MapInverse(0, y));
            AssertCovered(map, map.MapInverse(maxX, y));
        }
    }

    private static void AssertCovered(
        RenderGeometryMap map,
        GeometryPoint point)
    {
        Assert.True(double.IsFinite(point.X));
        Assert.True(double.IsFinite(point.Y));
        Assert.InRange(point.X, 0, map.SourceWidth - 1d);
        Assert.InRange(point.Y, 0, map.SourceHeight - 1d);
    }

    [Fact]
    public void UserCropOnCorrectedFrameLeavesNoBlankPixels()
    {
        using var source = CreateRedImage(400, 300);
        using var corrected = RenderGeometry.Apply(
            source,
            new EditSettings
            {
                HorizonRotation = 5,
                Geometry = new GeometrySettings
                {
                    Vertical = 50,
                    Horizontal = -40,
                    Aspect = 30,
                    Distortion = -60
                },
                Crop = new CropRegion
                {
                    Left = 0.1,
                    Top = 0.2,
                    Right = 0.8,
                    Bottom = 0.9
                }
            },
            out _);

        Assert.Equal(0, CountBlankPixels(corrected));
    }

    private static MagickImage CreateRedImage(uint width, uint height) =>
        new(MagickColors.Red, width, height)
        {
            ColorSpace = ColorSpace.RGB,
            Depth = 16
        };

    private static bool IsBlank(IMagickColor<ushort> color) =>
        !(color.R > Quantum.Max / 2 &&
          color.G < Quantum.Max / 2 &&
          color.B < Quantum.Max / 2);

    private static int CountBlankBorderPixels(MagickImage image)
    {
        using var pixels = image.GetPixels();
        var blank = 0;
        for (var x = 0; x < image.Width; x++)
        {
            if (IsBlank(pixels.GetPixel(x, 0).ToColor()!)) blank++;
            if (IsBlank(pixels.GetPixel(x, (int)image.Height - 1).ToColor()!)) blank++;
        }
        for (var y = 1; y < image.Height - 1; y++)
        {
            if (IsBlank(pixels.GetPixel(0, y).ToColor()!)) blank++;
            if (IsBlank(pixels.GetPixel((int)image.Width - 1, y).ToColor()!)) blank++;
        }
        return blank;
    }

    private static int CountBlankPixels(MagickImage image)
    {
        using var pixels = image.GetPixels();
        var blank = 0;
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
        {
            if (IsBlank(pixels.GetPixel(x, y).ToColor()!)) blank++;
        }
        return blank;
    }
}
