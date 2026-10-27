using HappyPhoton.LibRaw.Interop;
using Xunit;
using static HappyPhoton.Tests.RawBaseLoaderTestSupport;

namespace HappyPhoton.Tests;

public sealed class RawCameraFactsTests
{
    [Fact]
    public void CameraFacts_ExposeAvailableAbiV2ColorFacts()
    {
        using var context = LibRawContext.Open(Asset("canon-eos-350d.cr2"));
        context.Unpack();

        var facts = context.GetCameraFacts();

        Assert.NotNull(facts);
        Assert.Equal(facts!.Multipliers.Length, facts.PreMultipliers!.Length);
        Assert.All(facts.PreMultipliers, value =>
            Assert.True(float.IsFinite(value) && value > 0));
        Assert.Equal(facts.Multipliers.Length, facts.CameraFromXyz!.GetLength(0));
        Assert.Equal(3, facts.CameraFromXyz.GetLength(1));
        Assert.All(facts.CameraFromXyz.Cast<float>(), value =>
            Assert.True(float.IsFinite(value)));
        if (facts.LinearMax is { } linearMax)
        {
            Assert.Equal(facts.Multipliers.Length, linearMax.Length);
            Assert.All(linearMax, value => Assert.True(value > 0));
        }
    }
}
