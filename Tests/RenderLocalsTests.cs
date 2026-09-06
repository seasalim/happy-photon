using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RenderLocalsTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 2)]
    [InlineData(270, -3)]
    public void PixelCentersMatchCorrectedFrameOracle(int rotation, double horizon)
    {
        using var image = new MagickImage(MagickColors.Gray, 93, 61);
        var local = new LocalAdjustment { Cu = .42, Cv = .36, Angle = 37, Feather = .25, Exposure = 2 };
        var settings = new EditSettings { Rotation = rotation, HorizonRotation = horizon,
            Crop = new() { Left = .12, Top = .08, Right = .91, Bottom = .85 }, Locals = [local] };
        using var geometry = RenderGeometry.Apply(image, settings, out var trace);
        var frame = RenderGeometry.CalculateLocalsFrame(93, 61, settings);
        Assert.Equal(trace.CorrectedFrameWidth, frame.Width);
        Assert.Equal(trace.CorrectedFrameHeight, frame.Height);
        Assert.Equal(trace.CropX / (double)trace.CorrectedFrameWidth, frame.CropX);
        var width = 47;
        var height = 29;
        var plan = RenderLocals.Create(settings, trace, width, height)!;
        using var prepared = new MagickImage(MagickColors.Gray, (uint)width, (uint)height);
        using var preparedGeometry = RenderGeometry.Apply(prepared, new EditSettings(), out var preparedTrace);
        var restingPlan = RenderLocals.Create(settings, preparedTrace, width, height, frame)!;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var px = trace.CropX + (x + .5) * trace.Width / width;
            var py = trace.CropY + (y + .5) * trace.Height / height;
            var angle = local.Angle * Math.PI / 180;
            var s = ((px - local.Cu * trace.CorrectedFrameWidth) * Math.Cos(angle) +
                (py - local.Cv * trace.CorrectedFrameHeight) * Math.Sin(angle)) / frame.LongEdge;
            var t = Math.Clamp((s + local.Feather / 2) / local.Feather, 0, 1);
            var expected = 1 + (1 - t * t * (3 - 2 * t)) * 3;
            Assert.InRange(Math.Abs(expected - plan.Gain(y * width + x)), 0, 1e-12);
            Assert.InRange(Math.Abs(expected - restingPlan.Gain(y * width + x)), 0, 1e-12);
        }
    }

    [Fact]
    public void EightExtremeLocalsStayFiniteAndIdentityNeedsNoPlan()
    {
        using var image = new MagickImage(MagickColors.Gray, 40, 30);
        var settings = new EditSettings();
        using var geometry = RenderGeometry.Apply(image, settings, out var trace);
        Assert.Null(RenderLocals.Create(settings, trace, 40, 30));
        settings.Locals = Enumerable.Range(1, 8).Select(i => new LocalAdjustment
        { Ordinal = i, Cu = 2, Exposure = 4, Angle = 0, Feather = .001 }).ToList();
        var plan = RenderLocals.Create(settings, trace, 40, 30)!;
        for (var pixel = 0; pixel < 1200; pixel++)
            Assert.Equal(Math.Pow(2, 32), plan.Gain(pixel));
        foreach (var local in settings.Locals) local.Enabled = false;
        Assert.Null(RenderLocals.Create(settings, trace, 40, 30));
    }
}
