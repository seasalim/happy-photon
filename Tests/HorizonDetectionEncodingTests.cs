using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class HorizonDetectionEncodingTests
{
    [Fact]
    public void TransferInterpolationStaysWithinOneHundredthOfAnEncodedQuantum()
    {
        var maximum = 0d;
        var worstInput = 0d;
        var previous = 0d;

        // Sixteen probes in every Q16 interval, with extra coverage around the join.
        for (var i = 0; i <= 16 * ushort.MaxValue; i++)
        {
            var value = i / (16d * ushort.MaxValue);
            var actual = HorizonDetection.EncodeLuminance(value);
            var error = Math.Abs(actual - ToneLut.SrgbEncode(value));

            if (error > maximum)
            {
                maximum = error;
                worstInput = value;
            }

            Assert.True(actual >= previous);
            previous = actual;
        }

        Assert.True(maximum <= 1e-7, $"Maximum error {maximum:R} at {worstInput:R}");

        foreach (var value in new[] { 0d, .0031308, Math.BitIncrement(.0031308), 1d })
        {
            Assert.InRange(Math.Abs(HorizonDetection.EncodeLuminance(value) - ToneLut.SrgbEncode(value)), 0, 1e-7);
        }
    }
}
