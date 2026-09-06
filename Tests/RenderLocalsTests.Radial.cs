using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class RenderLocalsTests
{
    [Fact]
    public void MixedDocumentComposesIndependentWeights()
    {
        using var image = new MagickImage(MagickColors.Gray, 8, 6);
        var settings = new EditSettings { Locals =
        [
            new() { Type = "radial", Rx = .3, Ry = .2, Angle = 0, Feather = .5, Exposure = 2 },
            new() { Ordinal = 2, Angle = 0, Feather = .4, Exposure = -1 },
            new() { Type = "radial", Ordinal = 3, Rx = .3, Ry = .2, Angle = 0,
                Feather = .5, Outside = true, Exposure = -2 }
        ] };
        using var geometry = RenderGeometry.Apply(image, settings, out var trace);
        var plan = RenderLocals.Create(settings, trace, 8, 6)!;
        for (var y = 0; y < 6; y++) for (var x = 0; x < 8; x++)
        {
            var dx = (x + .5) / 8 - .5;
            var dy = (y + .5) / 8 - .375;
            var rho = Math.Sqrt(dx * dx / .09 + dy * dy / .04);
            var t = Math.Clamp((rho - .5) * 2, 0, 1);
            var radial = 1 - t * t * (3 - 2 * t);
            t = Math.Clamp((dx + .2) / .4, 0, 1);
            var linear = 1 - t * t * (3 - 2 * t);
            var expected = (1 + 3 * radial) * (1 - .5 * linear) * (1 - .75 * (1 - radial));
            Assert.InRange(Math.Abs(expected - plan.Gain(y * 8 + x)), 0, 1e-12);
        }
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(90, 2, .5)]
    [InlineData(270, -3, 1)]
    public void RadialPixelCentersMatchOracleOnBothPaths(int rotation, double horizon, double feather)
    {
        using var image = new MagickImage(MagickColors.Gray, 93, 61);
        var local = new LocalAdjustment { Type = "radial", Cu = .42, Cv = .36, Rx = .31, Ry = .17,
            Angle = 37, Feather = feather, Exposure = 2 };
        var settings = new EditSettings { Rotation = rotation, HorizonRotation = horizon,
            Crop = new() { Left = .12, Top = .08, Right = .91, Bottom = .85 }, Locals = [local] };
        using var geometry = RenderGeometry.Apply(image, settings, out var trace);
        var frame = RenderGeometry.CalculateLocalsFrame(93, 61, settings);
        const int width = 47, height = 29;
        using var prepared = new MagickImage(MagickColors.Gray, width, height);
        using var preparedGeometry = RenderGeometry.Apply(prepared, new EditSettings(), out var preparedTrace);
        var inside = RenderLocals.Create(settings, trace, width, height)!;
        var resting = RenderLocals.Create(settings, preparedTrace, width, height, frame)!;
        local.Outside = true;
        var outside = RenderLocals.Create(settings, trace, width, height)!;
        var outsideResting = RenderLocals.Create(settings, preparedTrace, width, height, frame)!;
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var dx = (trace.CropX + (x + .5) * trace.Width / width - local.Cu * frame.Width) / frame.LongEdge;
            var dy = (trace.CropY + (y + .5) * trace.Height / height - local.Cv * frame.Height) / frame.LongEdge;
            var theta = local.Angle * Math.PI / 180;
            var rho = Math.Sqrt(Math.Pow((dx * Math.Cos(theta) + dy * Math.Sin(theta)) / local.Rx, 2) +
                Math.Pow((-dx * Math.Sin(theta) + dy * Math.Cos(theta)) / local.Ry, 2));
            var t = feather == 0 ? (rho < 1 ? 0 : 1) : Math.Clamp((rho - 1 + feather) / feather, 0, 1);
            var weight = 1 - t * t * (3 - 2 * t);
            var pixel = y * width + x;
            Assert.InRange(Math.Abs(1 + 3 * weight - inside.Gain(pixel)), 0, 1e-12);
            Assert.InRange(Math.Abs(inside.Gain(pixel) - resting.Gain(pixel)), 0, 1e-12);
            Assert.InRange(Math.Abs(1 + 3 * (1 - weight) - outside.Gain(pixel)), 0, 1e-12);
            Assert.InRange(Math.Abs(outside.Gain(pixel) - outsideResting.Gain(pixel)), 0, 1e-12);
            Assert.Equal(5, inside.Gain(pixel) + outside.Gain(pixel), 12);
        }
    }
}
