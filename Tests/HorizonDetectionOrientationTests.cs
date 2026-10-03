using System.Reflection;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class HorizonDetectionOrientationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(4, 1, -4)]
    [InlineData(-4, 1, 4)]
    [InlineData(4, 2, 4)]
    public void CompetingOrientationsReturnTheWeightedPeak(double majorityAngle, double minorityWeight,
        double expected)
    {
        const int width = 42;
        const int height = 33;
        var gx = new double[width * height];
        var gy = new double[gx.Length];

        for (var sample = 0; sample < 10; sample++)
        {
            var angle = (sample < 6 ? majorityAngle : -majorityAngle) * Math.PI / 180;
            var magnitude = sample < 6 ? 1 : minorityWeight;
            var index = 16 * width + 16 + sample;
            gx[index] = magnitude * Math.Cos(angle);
            gy[index] = magnitude * Math.Sin(angle);
        }

        var orientation = typeof(HorizonDetection).GetMethod("Orientation", BindingFlags.Static | BindingFlags.NonPublic)!;
        var magnitudes = gx.Select((x, i) => Math.Sqrt(x * x + gy[i] * gy[i])).ToArray();
        var result = (HorizonDetection.Result)orientation.Invoke(null, [gx, gy, magnitudes, width, height, 1d, 1d])!;
        output.WriteLine($"majority={majorityAngle}, minorityWeight={minorityWeight}, correction={result.HorizonRotation}");

        Assert.Equal(expected, result.HorizonRotation, 8);
    }
}
