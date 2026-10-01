using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LensCorrectionProcessorTests
{
    [Fact]
    public void PlanRejectsAnAdditionalOrientationForAnOrientedSensorBuffer()
    {
        var prescription = new LensPrescription(LensPrescriptionSource.DngOpcode,
            null, [], [], LensFrameWindow.Full, LensFrameWindow.Full);

        for (var sensorOrientation = 2; sensorOrientation <= 8; sensorOrientation++)
        {
            for (var orientation = 2; orientation <= 8; orientation++)
            {
                var frame = new LensCorrectionReferenceFrame(12, 8, 12, 8, sensorOrientation);
                var error = Assert.Throws<ArgumentException>(() => new LensCorrectionPlan(
                    12, 8, 12, 8, orientation, prescription, BaseDecodeSettings.Default, 1, frame));
                Assert.Equal("orientation", error.ParamName);
            }
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 3)]
    [InlineData(1, 6)]
    [InlineData(1, 8)]
    [InlineData(3, 1)]
    [InlineData(6, 1)]
    [InlineData(8, 1)]
    public void PlanAcceptsOrientationOnOnlyOneFrame(int sensorOrientation, int orientation)
    {
        var prescription = new LensPrescription(LensPrescriptionSource.DngOpcode,
            null, [], [], LensFrameWindow.Full, LensFrameWindow.Full);
        var frame = new LensCorrectionReferenceFrame(12, 8, 12, 8, sensorOrientation);
        var plan = new LensCorrectionPlan(12, 8, 12, 8, orientation,
            prescription, BaseDecodeSettings.Default, 1, frame);

        Assert.True(plan.HasSharedGeometry);
    }
}
