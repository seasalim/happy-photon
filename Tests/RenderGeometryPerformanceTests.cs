using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RenderGeometryPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public RenderGeometryPerformanceTests(ITestOutputHelper output) =>
        _output = output;

    [Theory]
    [InlineData(1600, 1067)]
    [InlineData(6000, 4000)]
    public void GeometryStage_ReportsIdentityAndActiveLatency(int width, int height)
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to run geometry performance diagnostics.");
#if DEBUG
        Assert.Skip("Run geometry performance diagnostics in Release.");
#endif

        var identity = Measure(width, height, new EditSettings());
        var active = Measure(width, height, new EditSettings
        {
            HorizonRotation = 3,
            Geometry = new GeometrySettings
            {
                Vertical = 35,
                Horizontal = -30,
                Aspect = 25,
                Distortion = -40
            }
        });
        _output.WriteLine(
            $"geometry {width}x{height} identity={identity:F2}ms active={active:F2}ms");
        Assert.True(active <= (width <= 1600 ? 80 : 1_200),
            $"Geometry stage took {active:F2} ms.");
    }

    private static double Measure(int width, int height, EditSettings settings)
    {
        var samples = new List<double>();
        for (var iteration = 0; iteration < 9; iteration++)
        {
            using var image = new MagickImage(
                MagickColors.SlateGray, (uint)width, (uint)height)
            {
                ColorSpace = ColorSpace.RGB,
                Depth = 16
            };
            var stopwatch = Stopwatch.StartNew();
            using var corrected = RenderGeometry.Apply(image, settings, out _);
            stopwatch.Stop();
            if (iteration >= 3) samples.Add(stopwatch.Elapsed.TotalMilliseconds);
        }
        samples.Sort();
        return samples[samples.Count / 2];
    }
}
