using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class HealComparisonTests(ITestOutputHelper output)
{
    [Fact]
    public void MaskUsesExactlyTheSameRegionForControlAndRepair()
    {
        using var reference = RenderPipelineTestSupport.CreateBase([1000, 1000, 1000, 5000, 5000, 5000]);
        using var changedOutside = RenderPipelineTestSupport.CreateBase([1000, 1000, 1000, 65000, 0, 0]);
        var region = new[] { true, false };
        var masked = GoldenImageComparer.Compare(reference.Pixels, changedOutside.Pixels,
            GoldenComparisonDomain.LinearRec2020, region);
        Assert.Equal(new GoldenComparison(0, 0), masked);
        Assert.True(GoldenImageComparer.Compare(reference.Pixels, changedOutside.Pixels,
            GoldenComparisonDomain.LinearRec2020).MeanDeltaE > 0);
        Assert.Throws<ArgumentException>(() => GoldenImageComparer.Compare(reference.Pixels,
            changedOutside.Pixels, GoldenComparisonDomain.LinearRec2020, [false, false]));
    }

    [Fact]
    public void FrozenS64MatchesTheOracleIncludingOverlaps()
    {
        const int width = 160, height = 120;
        var random = new Random(27764);
        var input = Enumerable.Range(0, width * height * 3).Select(_ => (ushort)random.Next(65536)).ToArray();
        using var basis = RenderPipelineTestSupport.CreateBase(input, height: height);
        foreach (var candidate in HealCandidate.All)
        {
            var expected = HealOracle.Apply(input, width, height, HealWorkloads.S64(), candidate);
            using var actual = new HealPrototype().Repair(basis, HealWorkloads.S64(), candidate);
            var codes = RenderPipelineTestSupport.ReadPixels(actual.Pixels);
            var maximum = expected.Zip(codes, (a, b) => Math.Abs(a - b)).Max();
            output.WriteLine($"S64 oracle {candidate}: maximum_Q16_difference={maximum}, limit=1");
            Assert.InRange(maximum, 0, 1);
        }
    }
}
