using System.Reflection;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class HorizonDetectionOrientationBinTests
{
    [Fact]
    public void CertifiedBinsPreserveEveryHistogramWeightAcrossTheFullAngularRange()
    {
        var vote = typeof(HorizonDetection).GetMethod("VoteOrientation", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Action<double, double, double, double[]>>();
        var expected = new double[101];
        var actual = new double[101];
        var random = new Random(332);

        for (var sample = 0; sample < 100000; sample++)
        {
            var radians = (random.NextDouble() * 360 - 180) * Math.PI / 180;
            var magnitude = Math.ScaleB(1, random.Next(-900, 901));
            var dx = sample % 19 == 0 ? 0 : Math.Cos(radians) * magnitude;
            var dy = sample % 23 == 0 ? 0 : Math.Sin(radians) * magnitude;
            var weight = random.NextDouble();
            var angle = Math.Atan2(dy, dx) * 180 / Math.PI;
            angle -= 90 * Math.Floor((angle + 45) / 90);

            if (Math.Abs(angle) <= 5)
            {
                expected[(int)Math.Round((angle + 5) / .1)] += weight;
            }

            vote(dx, dy, weight, actual);
        }

        Assert.Equal(expected.Select(BitConverter.DoubleToInt64Bits), actual.Select(BitConverter.DoubleToInt64Bits));
    }
}
