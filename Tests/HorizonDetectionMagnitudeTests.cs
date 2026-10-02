using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class HorizonDetectionMagnitudeTests
{
    [Fact]
    public void HistogramSelectionMatchesSortedRanksIncludingTies()
    {
        var random = new Random(316);
        var values = Enumerable.Range(0, 10003).Select(i => i % 3 == 0 ? .125 :
            Math.ScaleB(random.NextDouble(), random.Next(-16, 0))).ToArray();
        var ordered = values.Order().ToArray();

        foreach (var rank in new[] { 0, 1, 4999, 5000, (int)((values.Length - 1) * .70), values.Length - 1 })
        {
            var selected = HorizonDetection.SelectMagnitude((double[])values.Clone(), values.Length, rank);
            Assert.Equal(BitConverter.DoubleToInt64Bits(ordered[rank]), BitConverter.DoubleToInt64Bits(selected));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10003)]
    public void HistogramSelectionRetainsAnIdenticalPopulationAndIgnoresUnusedCapacity(int count)
    {
        var values = new double[count + 13];
        Array.Fill(values, .125, 0, count);
        var selected = HorizonDetection.SelectMagnitude(values, count, (int)((count - 1) * .70));

        Assert.Equal(BitConverter.DoubleToInt64Bits(.125), BitConverter.DoubleToInt64Bits(selected));
    }
}
