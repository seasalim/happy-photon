using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class StraightenGateWp3BaselineTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void BestEffortOwnerArmAcceptsEveryTier(int tier)
    {
        using var frame = new MagickImage(MagickColors.Gray, 33, 33);
        var diagnostics = default(HorizonDetection.Diagnostics) with { Tier = (HorizonDetection.Tier)tier };
        var evaluation = StraightenGateWp3LinePredicate.Evaluate(diagnostics);

        VerifyAnswer("G3", BestEffortPhoto, frame, -.5, -1.144340,
            new HorizonDetection.Result(-1.14, 0), diagnostics, evaluation);
    }

    [Theory]
    [InlineData(1, -1.144340)]
    [InlineData(-5.1, -4.5)]
    public void BestEffortOwnerArmStillRejectsHarmAndOutOfRangeAnswers(double answer, double target)
    {
        using var frame = new MagickImage(MagickColors.Gray, 33, 33);
        var diagnostics = default(HorizonDetection.Diagnostics);
        var evaluation = StraightenGateWp3LinePredicate.Evaluate(diagnostics);

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => VerifyAnswer("G3", BestEffortPhoto,
            frame, -.5, target, new HorizonDetection.Result(answer, 0), diagnostics, evaluation));
    }
}
