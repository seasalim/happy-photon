using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(CheckpointCRenderGateCollection.Name)]
public sealed class RenderGeometryTests
{
    private readonly ITestOutputHelper _output;

    public RenderGeometryTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Apply_IdentityReturnsOwnedByteIdenticalImage()
    {
        using var source = CreateGradient(81, 53);
        var before = ReadPixels(source);

        using var actual = RenderGeometry.Apply(
            source,
            new EditSettings(),
            out var trace);

        Assert.NotSame(source, actual);
        Assert.Equal(before, ReadPixels(actual));
        Assert.Equal(before, ReadPixels(source));
        Assert.True(trace.Map.IsIdentity);
    }

    [Fact]
    public void Apply_QuarterTurnAndCropStayLosslessAndDoNotSample()
    {
        using var source = CreateGradient(80, 50);
        var passes = 0;
        GeometryWarpProcessor.SamplingPassStarted = () => passes++;
        try
        {
            using var actual = RenderGeometry.Apply(
                source,
                new EditSettings
                {
                    Rotation = 90,
                    Crop = new CropRegion
                    {
                        Left = 0.25,
                        Top = 0.2,
                        Right = 0.75,
                        Bottom = 0.8
                    }
                },
                out var trace);

            Assert.Equal(0, passes);
            Assert.Equal((uint)trace.Width, actual.Width);
            Assert.Equal((uint)trace.Height, actual.Height);
            Assert.True(trace.Width < trace.QuarterTurnWidth);
            Assert.True(trace.Height < trace.QuarterTurnHeight);
        }
        finally
        {
            GeometryWarpProcessor.SamplingPassStarted = null;
        }
    }

    [Theory]
    [InlineData(15, 0, 0, 0, 0)]
    [InlineData(0, -20, 0, 0, 0)]
    [InlineData(0, 0, 30, 0, 0)]
    [InlineData(0, 0, 0, -40, 0)]
    [InlineData(15, -20, 30, -40, 3)]
    public void Apply_AnyContinuousGeometryUsesOneSamplingPass(
        int vertical,
        int horizontal,
        int aspect,
        int distortion,
        double horizon)
    {
        using var source = CreateGradient(160, 107);
        var passes = 0;
        GeometryWarpProcessor.SamplingPassStarted = () => passes++;
        try
        {
            using var actual = RenderGeometry.Apply(
                source,
                new EditSettings
                {
                    HorizonRotation = horizon,
                    Geometry = new GeometrySettings
                    {
                        Vertical = vertical,
                        Horizontal = horizontal,
                        Aspect = aspect,
                        Distortion = distortion
                    }
                },
                out _);

            Assert.Equal(1, passes);
            Assert.True(actual.Width <= source.Width);
            Assert.True(actual.Height <= source.Height);
        }
        finally
        {
            GeometryWarpProcessor.SamplingPassStarted = null;
        }
    }

    [Theory]
    [InlineData(100)]
    [InlineData(-100)]
    public void Map_ForwardAndInverseRoundTripAcrossRadialContinuation(int distortion)
    {
        var map = new RenderGeometryMap(
            400,
            300,
            3,
            new GeometrySettings
            {
                Vertical = 55,
                Horizontal = -45,
                Aspect = 70,
                Distortion = distortion
            });

        foreach (var y in new[] { 0d, (map.OutputHeight - 1) / 2d, map.OutputHeight - 1d })
        foreach (var x in new[] { 0d, (map.OutputWidth - 1) / 2d, map.OutputWidth - 1d })
        {
            var source = map.MapInverse(x, y);
            var corrected = map.MapForward(source.X, source.Y);
            Assert.InRange(Math.Abs(corrected.X - x), 0, 1e-8);
            Assert.InRange(Math.Abs(corrected.Y - y), 0, 1e-8);
        }
    }

    [Fact]
    public void Map_Vertical100UsesDocumentedNegativeHalfCoefficient()
    {
        var map = new RenderGeometryMap(
            401,
            301,
            0,
            new GeometrySettings { Vertical = 100 });
        var outputCenterX = (map.OutputWidth - 1) / 2d;
        var outputCenterY = (map.OutputHeight - 1) / 2d;

        var source = map.MapInverse(outputCenterX, outputCenterY + 150);

        Assert.Equal(200, source.X, 10);
        Assert.Equal(450, source.Y, 10);
    }

    [Fact]
    public void Apply_ActiveGeometryBilinearlySamplesAlpha()
    {
        using var source = CreateAlphaGradient(81, 53);
        var settings = new EditSettings
        {
            Geometry = new GeometrySettings
            {
                Vertical = 35,
                Horizontal = -20,
                Aspect = 25,
                Distortion = -30
            }
        };

        using var actual = RenderGeometry.Apply(source, settings, out var trace);
        var x = trace.Width / 3;
        var y = trace.Height / 3;
        var sourcePoint = trace.Map.MapInverse(x, y);
        var expected = SampleAlpha(source, sourcePoint.X, sourcePoint.Y);
        var rgba = actual.GetPixelsUnsafe().ToShortArray(PixelMapping.RGBA) ??
            throw new InvalidOperationException("Unable to read RGBA pixels.");
        var actualAlpha = rgba[(y * trace.Width + x) * 4 + 3];

        Assert.True(actual.HasAlpha);
        Assert.InRange(Math.Abs(actualAlpha - expected), 0, 1);
    }

    [Fact]
    public void Apply_KnownRuledCoordinateWarpInvertsWithinAccuracyGate()
    {
        var settings = new EditSettings
        {
            Geometry = new GeometrySettings
            {
                Vertical = 55,
                Horizontal = -45,
                Distortion = 70
            }
        };
        var map = new RenderGeometryMap(400, 300, 0, settings.Geometry);
        using var injected = CreateCoordinateGrid(map);
        using var corrected = RenderGeometry.Apply(injected, settings, out _);
        var pixels = ReadPixels(corrected);
        var maximumResidual = 0d;
        for (var line = 0; line <= 10; line++)
        for (var sample = 0; sample <= 40; sample++)
        {
            var x = (int)Math.Round(line * (corrected.Width - 1) / 10d);
            var y = (int)Math.Round(sample * (corrected.Height - 1) / 40d);
            maximumResidual = Math.Max(maximumResidual,
                ReadCoordinateError(pixels, (int)corrected.Width, (int)corrected.Height, x, y));
        }

        var cornerError = ReadCoordinateError(
            pixels,
            (int)corrected.Width,
            (int)corrected.Height,
            (int)corrected.Width - 1,
            (int)corrected.Height - 1);

        _output.WriteLine(
            $"Maximum line residual: {maximumResidual:F4} px; " +
            $"corner error: {cornerError:F4} px.");
        Assert.InRange(maximumResidual, 0, 0.75);
        Assert.InRange(cornerError, 0, 1.5);
    }

    private static MagickImage CreateCoordinateGrid(RenderGeometryMap map)
    {
        var image = new MagickImage(MagickColors.Black, (uint)map.SourceWidth, (uint)map.SourceHeight)
        {
            ColorSpace = ColorSpace.RGB,
            Depth = 16
        };
        using var pixels = image.GetPixels();
        for (var y = 0; y < map.SourceHeight; y++)
        for (var x = 0; x < map.SourceWidth; x++)
        {
            var coordinate = map.MapForward(x, y);
            var red = EncodeCoordinate(coordinate.X, map.OutputWidth);
            var green = EncodeCoordinate(coordinate.Y, map.OutputHeight);
            pixels.SetPixel(x, y, [red, green, 0]);
        }
        return image;
    }

    private static ushort EncodeCoordinate(double coordinate, int extent)
    {
        if (!double.IsFinite(coordinate))
            return 0;

        var normalized = Math.Clamp(coordinate / Math.Max(1, extent - 1), 0, 1);
        return (ushort)Math.Round(normalized * ushort.MaxValue);
    }

    private static double ReadCoordinateError(
        ushort[] pixels,
        int width,
        int height,
        int x,
        int y)
    {
        var offset = (y * width + x) * 3;
        var measuredX = pixels[offset] / (double)ushort.MaxValue * (width - 1);
        var measuredY = pixels[offset + 1] / (double)ushort.MaxValue * (height - 1);
        return Math.Sqrt(Math.Pow(measuredX - x, 2) + Math.Pow(measuredY - y, 2));
    }

    private static MagickImage CreateGradient(int width, int height)
    {
        var image = new MagickImage(MagickColors.Black, (uint)width, (uint)height)
        {
            ColorSpace = ColorSpace.RGB,
            Depth = 16
        };
        using var pixels = image.GetPixels();
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var red = (ushort)(x * ushort.MaxValue / Math.Max(1, width - 1));
            var green = (ushort)(y * ushort.MaxValue / Math.Max(1, height - 1));
            pixels.SetPixel(x, y, [red, green, (ushort)(red / 2 + green / 2)]);
        }
        return image;
    }

    private static MagickImage CreateAlphaGradient(int width, int height)
    {
        var image = new MagickImage(
            MagickColors.Transparent,
            (uint)width,
            (uint)height)
        {
            ColorSpace = ColorSpace.RGB,
            Depth = 16
        };
        using var pixels = image.GetPixels();
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            pixels.SetPixel(x, y,
            [
                (ushort)(x * ushort.MaxValue / Math.Max(1, width - 1)),
                (ushort)(y * ushort.MaxValue / Math.Max(1, height - 1)),
                30000,
                (ushort)(5000 + x * 500 + y * 250)
            ]);
        }
        return image;
    }

    private static int SampleAlpha(MagickImage image, double x, double y)
    {
        x = Math.Clamp(x, 0, image.Width - 1);
        y = Math.Clamp(y, 0, image.Height - 1);
        var x0 = (int)x;
        var y0 = (int)y;
        var x1 = Math.Min((int)image.Width - 1, x0 + 1);
        var y1 = Math.Min((int)image.Height - 1, y0 + 1);
        var fx = x - x0;
        var fy = y - y0;
        using var pixels = image.GetPixels();
        double Alpha(int sampleX, int sampleY) =>
            pixels.GetPixel(sampleX, sampleY).ToColor()!.A;
        var top = Alpha(x0, y0) + (Alpha(x1, y0) - Alpha(x0, y0)) * fx;
        var bottom = Alpha(x0, y1) + (Alpha(x1, y1) - Alpha(x0, y1)) * fx;
        return (int)Math.Round(top + (bottom - top) * fy);
    }

    private static ushort[] ReadPixels(MagickImage image) =>
        image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGB) ??
        throw new InvalidOperationException("Unable to read RGB pixels.");
}
