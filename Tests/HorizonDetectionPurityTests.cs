using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class HorizonDetectionPurityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("horizon", false, -3)]
    [InlineData("facade-blurred", true, 1.5)]
    [InlineData("interior", false, 5)]
    public void RepeatedCallsPreserveInputAndResultBits(string scene, bool portrait, double tilt)
    {
        using var basis = StraightenGateScenes.Create(scene, portrait);
        using var frame = StraightenGateScenes.Tilt(basis, tilt);
        var before = Hash(frame);
        var expected = HorizonDetection.Detect(frame, out var expectedDiagnostics);
        Assert.NotNull(expected);

        for (var call = 0; call < 3; call++)
        {
            var actual = HorizonDetection.Detect(frame, out var diagnostics);
            Assert.Equal(expected, actual);
            Assert.Equal(expectedDiagnostics, diagnostics);
            Assert.Equal(before, Hash(frame));
            output.WriteLine("STRAIGHTEN " + JsonSerializer.Serialize(new { gate = "purity",
                pid = Environment.ProcessId, values = new { scene, portrait, tilt, call, inputHash = before,
                    angleBits = BitConverter.DoubleToInt64Bits(actual!.Value.HorizonRotation),
                    confidenceBits = BitConverter.DoubleToInt64Bits(actual.Value.Confidence), diagnostics } }));
        }
    }

    private static string Hash(MagickImage image)
    {
        var pixels = RenderPipelineTestSupport.ReadPixels(image);

        return Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(pixels.AsSpan())));
    }
}
