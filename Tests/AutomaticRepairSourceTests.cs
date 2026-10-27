using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class AutomaticRepairSourceTests
{
    [Theory]
    [InlineData(1600, 1067)]
    [InlineData(1600, 120)]
    [InlineData(120, 1600)]
    public void RankedSourcesAreDeterministicInsideAndNonoverlapping(int width, int height)
    {
        using var pixels = new MagickImage("gradient:#112233-#ccbbaa", (uint)width, (uint)height);
        using var basis = new BaseImage(pixels, new BaseImageInfo(BaseSourceKind.Standard, false,
            BaseDecodeSettings.Default, null, null, 6504, 0, false, null, 1, width, height));
        var spots = RepairTestWorkload.S64();
        spots.AddRange(new[] { (0d, 0d), (1d, 1d), (0d, 1d), (1d, 0d), (.5, .5) }
            .Select(p => new Repair { U = p.Item1, V = p.Item2, Radius = .1 }));
        foreach (var spot in spots)
        {
            var candidates = AutomaticRepairSource.Rank(basis, spot);
            Assert.NotEmpty(candidates);
            Assert.Equal(candidates, AutomaticRepairSource.Rank(basis, spot));
            Assert.Equal(candidates.OrderBy(candidate => candidate.Score), candidates);
            foreach (var source in candidates)
            {
                var placed = spot with { Su = source.U, Sv = source.V };
                Assert.Equal((source.U, source.V), RepairGeometry.ClampSource(placed, width, height));
                var distance = Math.Sqrt(Math.Pow((spot.U - source.U) * width, 2) + Math.Pow((spot.V - source.V) * height, 2));
                Assert.True(distance >= 2 * RepairGeometry.EffectiveRadius(spot.Radius, width, height));
            }
        }
    }
}
