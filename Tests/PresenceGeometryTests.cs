using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PresenceGeometryTests
{
    [Theory]
    [InlineData(768, 100, 0)]
    [InlineData(768, 0, 12)]
    [InlineData(192, 100, 0)]
    [InlineData(192, 0, 12)]
    public void WarpUsesBaseCellSizeAndRadius(int longEdge, int vertical, double straighten)
    {
        var height = longEdge * 2 / 3;
        using var basis = RenderPipelineTestSupport.CreateBase(
            OpsKernelTests.Sentinel(longEdge, height), height: height);
        var settings = new EditSettings
        {
            Geometry = new() { Vertical = vertical }, HorizonRotation = straighten,
            Detail = new() { CaptureSharpen = 0 }
        };
        var frame = RenderGeometry.CalculateLocalsFrame(longEdge, height, settings);
        Assert.True(frame.LongEdge < longEdge);
        Assert.Equal(longEdge, frame.BaseLongEdge);

        // A geometric warp changes content and boundaries, so assert support numerically.
        // Also exercise the unchanged override after the resting image is downsampled.
        foreach (var target in new[] { (int)frame.LongEdge, (int)frame.LongEdge / 2 })
        {
            var width = target;
            var rows = (int)Math.Round(frame.Height * target / frame.Width);
            var renderedBaseEdge = longEdge * (target / frame.Width);
            var gridScale = Math.Min(1, 256 / renderedBaseEdge);
            var grid = PresenceGrid.Reduce(width, rows, _ => .3, null, frame);
            Assert.Equal((int)Math.Round(width * gridScale), grid.Width);
            Assert.Equal((int)Math.Round(rows * gridScale), grid.Height);
            Assert.Equal((int)Math.Ceiling(Math.Sqrt(3) * .010 * Math.Min(256, renderedBaseEdge)),
                grid.GuidedRadius);
        }

        // Verify that the pipeline supplies the same pre-warp extent as resting preparation.
        var pipeline = new RenderPipeline();
        using var expected = pipeline.RenderDisplayRec2020(
            new(basis, settings, RenderIntent.Export, null, new(false)));
        settings.Clarity = 100;
        RenderPresence.Apply(expected, basis.Info, settings, frame: frame);
        using var actual = pipeline.RenderDisplayRec2020(
            new(basis, settings, RenderIntent.Export, null, new(false)));
        Assert.Equal(RenderPipelineTestSupport.ReadPixels(expected), RenderPipelineTestSupport.ReadPixels(actual));
    }
}
