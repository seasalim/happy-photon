using System.Reflection;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class HorizonDetectionFallbackEquivalenceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(257)]
    [InlineData(523776)]
    public void UpperMedianIgnoresUnusedScratchAndHandlesOrderedAndTiedValues(int count)
    {
        var random = new Random(332);
        var values = Enumerable.Range(0, count).Select(_ => random.NextDouble() * 2 - 1).ToArray();
        var ordered = values.Order().ToArray();

        foreach (var input in new[] { values, ordered, ordered.Reverse().ToArray(),
            Enumerable.Repeat(.25, count).ToArray(), values.Select(x => (double)(int)(x * 10)).ToArray() })
        {
            var scratch = input.Concat(Enumerable.Repeat(double.NaN, 19)).ToArray();
            var expected = input.Order().ElementAt(count / 2);
            var actual = (double)Method("Median").Invoke(null, [scratch, count])!;
            EqualBits(expected, actual);
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1.5625, 1.562225475841874)]
    [InlineData(1.562225475841874, 1.5625)]
    [InlineData(2, 1)]
    public void OrientationPreservesBitsAtEveryBinAndWindowBoundary(double sx, double sy)
    {
        const int width = 42;
        const int height = 33;
        var gx = new double[width * height];
        var gy = new double[gx.Length];
        var random = new Random(332);

        // Exercise both signs of both axes, half-bin ties and either side of the
        // exact window edge. Other pixels retain off-window and sub-quantum weight.
        for (var axis = -180; axis <= 180; axis += 90)
        {
            for (var halfBin = -101; halfBin <= 101; halfBin++)
            {
                foreach (var epsilon in new[] { -1e-8, -1e-10, -1e-12, 0, 1e-12, 1e-10, 1e-8 })
                {
                    for (var sample = 0; sample < 10; sample++)
                    {
                        var angle = (sample < 6 ? axis + halfBin * .05 + epsilon :
                            random.NextDouble() * 360 - 180) * Math.PI / 180;
                        var weight = sample switch { 7 => 0, 8 => 1e-6, _ => random.NextDouble() };
                        var index = 16 * width + 16 + sample;
                        gx[index] = weight * Math.Cos(angle) * sx;
                        gy[index] = weight * Math.Sin(angle) * sy;
                    }

                    CompareOrientation(gx, gy, width, height, sx, sy);
                }
            }
        }
    }

    [Theory]
    [InlineData("flat")]
    [InlineData("random")]
    [InlineData("tied")]
    public void SkylinePreservesBitsAcrossScratchBufferSizesAndContents(string scene)
    {
        var random = new Random(332);

        // Large/small/large calls catch stale pooled tails and the two possible
        // parities of the all-pairs slope count. Ties preserve the upper median.
        foreach (var width in new[] { 1024, 33, 683, 64, 768, 34, 1024 })
        {
            const int height = 97;
            var plane = new double[width * height];

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    plane[y * width + x] = scene switch
                    {
                        "flat" => .4,
                        "tied" => y < 30 + x % 7 ? .7 : .2,
                        _ => random.NextDouble()
                    };
                }
            }

            var expected = HorizonDetectionFallbackReference.Skyline(plane, width, height, 1.5625, 1.563);
            var actual = (HorizonDetection.SkylineDiagnostics)Method("Skyline").Invoke(null,
                [plane, width, height, 1.5625, 1.563])!;
            EqualBits(expected.Share, actual.Share);
            EqualBits(expected.Span, actual.Span);
            EqualBits(expected.ContentAngle, actual.ContentAngle);
        }
    }

    [Fact]
    public void ReusedCannyMagnitudesMatchTheOriginalOrientationFormula()
    {
        const int width = 80;
        const int height = 67;
        var random = new Random(332);
        var gx = Enumerable.Range(0, width * height).Select(_ => random.NextDouble() - .5).ToArray();
        var gy = Enumerable.Range(0, gx.Length).Select(_ => random.NextDouble() - .5).ToArray();
        var magnitudes = new double[gx.Length];
        Method("Canny").Invoke(null, [gx, gy, width, height, magnitudes]);

        for (var y = 16; y < height - 16; y++)
        {
            for (var x = 16; x < width - 16; x++)
            {
                var i = y * width + x;
                EqualBits(Math.Sqrt(gx[i] * gx[i] + gy[i] * gy[i]), magnitudes[i]);
            }
        }

        CompareOrientation(gx, gy, width, height, 1, 1, magnitudes);
    }

    private static void CompareOrientation(double[] gx, double[] gy, int width, int height,
        double sx, double sy, double[]? magnitudes = null)
    {
        magnitudes ??= gx.Select((x, i) => Math.Sqrt(x * x + gy[i] * gy[i])).ToArray();
        var expected = HorizonDetectionFallbackReference.Orientation(gx, gy, width, height, sx, sy);
        var actual = (HorizonDetection.Result)Method("Orientation").Invoke(null,
            [gx, gy, magnitudes, width, height, sx, sy])!;
        EqualBits(expected.HorizonRotation, actual.HorizonRotation);
        EqualBits(expected.Confidence, actual.Confidence);
    }

    private static MethodInfo Method(string name) => typeof(HorizonDetection).GetMethod(name,
        BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void EqualBits(double expected, double actual) =>
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
}
