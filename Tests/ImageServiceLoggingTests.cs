using HappyPhoton.Services;
using Xunit;
using static HappyPhoton.Services.ImageServiceHelpers;

namespace HappyPhoton.Tests;

public sealed class ImageServiceLoggingTests
{
    [Fact]
    public void DisabledDebugLogging_DoesNotEvaluateInterpolatedValues()
    {
        if (DebugLoggingEnabled) return;
        var evaluations = 0;

        LogDebug("test", $"value={Increment(ref evaluations)}");

        Assert.Equal(0, evaluations);
    }

    [Fact]
    public void DisabledPerformanceLogging_DoesNotEvaluateInterpolatedExtra()
    {
        if (PerfLoggingEnabled) return;
        var evaluations = 0;

        LogPerformance("test", "step", 1, null, $"value={Increment(ref evaluations)}");

        Assert.Equal(0, evaluations);
    }

    private static int Increment(ref int value) => ++value;
}
