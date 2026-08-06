using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RawExposureBiasTests
{
    [Fact]
    public void FromFuji_UsesExpoMidPointShift()
    {
        var actual = RawExposureBias.FromFuji(-0.58f, ushort.MaxValue);

        Assert.Equal(0.58, actual, 6);
    }

    [Theory]
    [InlineData(-999f, 65535, 0)]
    [InlineData(-999f, 200, 1)]
    [InlineData(-999f, 400, 2)]
    [InlineData(-999f, 100, 0)]
    [InlineData(-999f, 0, 0)]
    [InlineData(-5f, 65535, 3)]
    [InlineData(5f, 65535, -3)]
    [InlineData(0f, 400, 0)]
    public void FromFuji_HandlesFallbacksAndBounds(
        float expoMidPointShift,
        ushort developmentDynamicRange,
        double expected)
    {
        var actual = RawExposureBias.FromFuji(
            expoMidPointShift,
            developmentDynamicRange);

        Assert.Equal(expected, actual, 6);
    }

    [Fact]
    public void FromFuji_NonFiniteShiftUsesDynamicRangeFallback()
    {
        var actual = RawExposureBias.FromFuji(float.NaN, 200);

        Assert.Equal(1, actual);
    }
}
