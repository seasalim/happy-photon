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
    [InlineData("skyline", false, -3)]
    [InlineData("skyline", true, 1.5)]
    [InlineData("fbm", false, 0)]
    public void RepeatedCallsPreserveInputAndResultBits(string scene, bool portrait, double tilt)
    {
        using var basis = scene == "fbm" ? StraightenGateNegativeScenes.Create(scene, portrait) :
            StraightenGateScenes.Create(scene, portrait);
        using var frame = StraightenGateScenes.Tilt(basis, tilt);
        var before = Hash(frame);
        var expected = HorizonDetection.Detect(frame, out var expectedDiagnostics);

        for (var call = 0; call < 3; call++)
        {
            var actual = HorizonDetection.Detect(frame, out var diagnostics);
            Assert.Equal(expected, actual);
            Assert.Equal(expectedDiagnostics, diagnostics);
            Assert.Equal(before, Hash(frame));
            output.WriteLine("STRAIGHTEN " + JsonSerializer.Serialize(new { gate = "purity",
                pid = Environment.ProcessId, values = new { scene, portrait, tilt, call, inputHash = before,
                    angleBits = BitConverter.DoubleToInt64Bits(actual.HorizonRotation),
                    confidenceBits = BitConverter.DoubleToInt64Bits(actual.Confidence), diagnostics } }));
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(32, 1024)]
    public void FramesWithoutAnInteriorReturnZeroOrientation(uint width, uint height)
    {
        using var frame = new MagickImage(MagickColors.Gray, width, height);
        var result = HorizonDetection.Detect(frame, out var diagnostics);

        Assert.Equal(new HorizonDetection.Result(0, 0), result);
        Assert.Equal(HorizonDetection.Tier.Orientation, diagnostics.Tier);
    }

    private static string Hash(MagickImage image)
    {
        var pixels = RenderPipelineTestSupport.ReadPixels(image);

        return Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(pixels.AsSpan())));
    }
}
