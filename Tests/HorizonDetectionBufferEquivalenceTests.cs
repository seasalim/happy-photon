using System.Buffers;
using System.Collections;
using System.Reflection;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class HorizonDetectionBufferEquivalenceTests
{
    [Theory]
    [InlineData("random")]
    [InlineData("flat")]
    [InlineData("steps")]
    public void DirtyOversizedBuffersPreserveSamplingGradientsAndEdges(string scene)
    {
        var random = new Random(332);

        // Cross pool buckets and vector tails, then revisit a large allocation.
        foreach (var (width, height) in new[] { (1024, 683), (33, 34), (67, 80), (768, 1024), (1024, 684) })
        {
            var sourceWidth = width * 3 / 2;
            var sourceHeight = height * 3 / 2;
            var samples = Enumerable.Range(0, sourceWidth * sourceHeight * 3).Select(i => scene switch
            {
                "flat" => (ushort)20000,
                "steps" => (ushort)((i / 3 / sourceWidth % 13 < 6) ? 10000 : 50000),
                _ => (ushort)random.Next(65536)
            }).ToArray();
            using var basis = RenderPipelineTestSupport.CreateBase(samples, height: sourceHeight);
            var expectedPlane = HorizonDetectionBufferReference.ReadLuminance(basis.Pixels, width, height);
            var (expectedX, expectedY) = HorizonDetectionBufferReference.Gradients(
                (double[])expectedPlane.Clone(), width, height);
            var expectedEdges = HorizonDetectionBufferReference.Canny(expectedX, expectedY, width, height,
                out var expectedMagnitude);
            var count = width * height;
            var plane = Enumerable.Repeat(double.NaN, count + 19).ToArray();
            var gx = (double[])plane.Clone();
            var gy = (double[])plane.Clone();
            var magnitude = (double[])plane.Clone();
            PoisonPool<double>(count, double.NaN);
            PoisonPool<byte>(count, 2);
            PoisonPool<int>(count, int.MaxValue);
            PoisonPool<int>(1 << 16, int.MaxValue);

            Method("ReadLuminance").Invoke(null, [basis.Pixels, width, height, plane]);
            Method("Gradients").Invoke(null, [plane, width, height, gx, gy]);
            var edges = ((IEnumerable)Method("Canny").Invoke(null, [gx, gy, width, height, magnitude])!)
                .Cast<object>().ToArray();

            EqualBits(expectedPlane, plane.Take(count));
            EqualBits(Interior(expectedX, width, height), Interior(gx, width, height));
            EqualBits(Interior(expectedY, width, height), Interior(gy, width, height));
            EqualBits(expectedMagnitude, magnitude.Take(count));
            Assert.Equal(expectedEdges.Count, edges.Length);

            for (var i = 0; i < edges.Length; i++)
            {
                var expected = expectedEdges[i];
                var edge = edges[i];
                var type = edge.GetType();
                EqualBits([expected.X, expected.Y, expected.Tilt], new[] { "X", "Y", "Tilt" }
                    .Select(name => (double)type.GetProperty(name)!.GetValue(edge)!));
                Assert.Equal(expected.Vertical, (bool)type.GetProperty("Vertical")!.GetValue(edge)!);
            }

            foreach (var buffer in new[] { plane, gx, gy, magnitude })
            {
                Assert.All(buffer.Skip(count), value => Assert.True(double.IsNaN(value)));
            }
        }
    }

    [Fact]
    public void CandidateTracingFailureDoesNotAffectFollowingDetection()
    {
        using var basis = StraightenGateScenes.Create("horizon", false);
        var expected = HorizonDetection.Detect(basis, out var expectedDiagnostics);
        Assert.Throws<InvalidOperationException>(() => HorizonDetection.Detect(basis, out _,
            _ => throw new InvalidOperationException("trace failure")));
        var actual = HorizonDetection.Detect(basis, out var actualDiagnostics);

        Assert.Equal(expected, actual);
        Assert.Equal(expectedDiagnostics, actualDiagnostics);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PoisonedPoolPreservesBoundaryStepResultAndDiagnostics(bool vertical, bool portrait)
    {
        var width = portrait ? 40 : 640;
        var height = portrait ? 640 : 40;
        // The last edge-extraction coordinate samples the unwritten magnitude ring.
        // Landscape's horizontal arm is the review's 640 x 40 step at row 22.
        var boundary = (vertical ? width : height) - 18;
        var samples = Enumerable.Range(0, width * height * 3).Select(i =>
            (ushort)((vertical ? i / 3 % width : i / 3 / width) <= boundary ? 50000 : 10000)).ToArray();
        using var basis = RenderPipelineTestSupport.CreateBase(samples, height: height);
        HorizonDetection.Result poisonedResult = default;
        HorizonDetection.Diagnostics poisonedDiagnostics = default;

        foreach (var poison in new[] { double.NaN, 0d })
        {
            PoisonPool<double>(width * height, poison);
            PoisonPool<double>(width * (width - 1) / 2, poison);
            PoisonPool<double>(width, poison);
            PoisonPool<byte>(width * height, poison == 0 ? (byte)0 : (byte)2);
            PoisonPool<int>(width * height, poison == 0 ? 0 : int.MaxValue);
            PoisonPool<int>(1 << 16, poison == 0 ? 0 : int.MaxValue);
            var result = HorizonDetection.Detect(basis.Pixels, out var diagnostics);

            if (double.IsNaN(poison))
            {
                poisonedResult = result;
                poisonedDiagnostics = diagnostics;
            }
            else
            {
                EqualValueBits(result, poisonedResult);
                EqualValueBits(diagnostics, poisonedDiagnostics);
            }
        }
    }

    private static void EqualValueBits(object expected, object actual)
    {
        if (expected is double value)
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(value), BitConverter.DoubleToInt64Bits((double)actual));

            return;
        }

        var type = expected.GetType();

        if (type.IsPrimitive || type.IsEnum)
        {
            Assert.Equal(expected, actual);

            return;
        }

        foreach (var property in type.GetProperties())
        {
            EqualValueBits(property.GetValue(expected)!, property.GetValue(actual)!);
        }
    }

    private static void PoisonPool<T>(int count, T value)
    {
        var buffers = Enumerable.Range(0, 8).Select(_ => ArrayPool<T>.Shared.Rent(count)).ToArray();

        foreach (var buffer in buffers)
        {
            Array.Fill(buffer, value);
            ArrayPool<T>.Shared.Return(buffer);
        }
    }

    private static IEnumerable<double> Interior(double[] values, int width, int height) =>
        Enumerable.Range(16, height - 32).SelectMany(y =>
            values.Skip(y * width + 16).Take(width - 32));

    private static void EqualBits(IEnumerable<double> expected, IEnumerable<double> actual) =>
        Assert.Equal(expected.Select(BitConverter.DoubleToInt64Bits), actual.Select(BitConverter.DoubleToInt64Bits));

    private static MethodInfo Method(string name) => typeof(HorizonDetection).GetMethod(name,
        BindingFlags.Static | BindingFlags.NonPublic)!;
}
