using Avalonia;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RepairSamplingTests
{
    [Fact]
    public void RangeHueAndWhiteBalanceSamplingSeeRepairedPixels()
    {
        using var basis = RenderRepairsTests.Fixture();
        var settings = RepairRenderEntryTests.Settings(); settings.Crop = null;
        using var repaired = new HealProductionStage().Repair(basis, [new(.6, .5, .2, .3, .1, true, 0)], default);
        var neutral = settings.Clone(); neutral.Repairs = null;
        using var prepared = LocalRangeSampling.Prepare(basis, settings, new PixelSize(160, 120), out _, out _, out _);
        Assert.Equal(RenderPipelineTestSupport.ReadPixels(repaired.Pixels), RenderPipelineTestSupport.ReadPixels(prepared));
        var pick = LocalRangeSampling.Pick(basis, settings, new Point(.6, .5), new PixelSize(160, 120));
        var expected = LocalRangeSampling.Pick(repaired, neutral, new Point(.6, .5), new PixelSize(160, 120));
        Assert.Null(pick.Rejection); Assert.Equal(expected, pick);
        Assert.NotEqual(LocalRangeSampling.Pick(basis, neutral, new Point(.6, .5), new PixelSize(160, 120)), pick);
        var gains = WhiteBalanceSampling.PickGains(basis.Pixels, settings, .6, .5);
        Assert.NotNull(gains);
        Assert.Equal(WhiteBalanceSampling.PickGains(repaired.Pixels, neutral, .6, .5), gains);
        Assert.NotEqual(WhiteBalanceSampling.PickGains(basis.Pixels, neutral, .6, .5), gains);
    }
}
