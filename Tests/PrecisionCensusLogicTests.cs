using Xunit;

namespace HappyPhoton.Tests;

internal enum PrecisionCensusTerminal
{
    Clean,
    Loss,
    Invalid
}

internal sealed record PrecisionTerminalEvidence(
    bool Valid,
    bool WorkingQualityAvailable,
    int LongestRecoverableWorkingRun,
    double WorkingP99DeltaE00,
    bool PhaseZeroThresholdCrossed,
    bool PlannedStageContractLoss,
    bool IndeterminateCouldBeMaterial,
    int IngressRecoverableRun = 0,
    double IngressP99DeltaE00 = 0);

internal static class PrecisionCensusLogic
{
    internal const double MinimumUseful = 8 / (double)byte.MaxValue;
    internal const double MaximumUseful = 247 / (double)byte.MaxValue;

    public static PrecisionClipDirection ClassifyClip(double reference) =>
        reference < 0
            ? PrecisionClipDirection.Negative
            : reference > 1
                ? PrecisionClipDirection.AboveWhite
                : PrecisionClipDirection.None;

    public static PrecisionRecovery DetermineRecovery(
        PrecisionClipDirection clip,
        bool remainingStagesAreAnalytic,
        double? finalReference)
    {
        if (clip == PrecisionClipDirection.None)
        {
            return PrecisionRecovery.NotApplicable;
        }
        if (clip == PrecisionClipDirection.Negative ||
            !remainingStagesAreAnalytic ||
            finalReference is not { } final)
        {
            return PrecisionRecovery.Indeterminate;
        }
        return IsUseful(final)
            ? PrecisionRecovery.ReturnsUseful
            : PrecisionRecovery.DoesNotReturn;
    }

    public static int LongestContiguousRun(IReadOnlyList<bool> included)
    {
        var longest = 0;
        var current = 0;
        foreach (var value in included)
        {
            current = value ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }
        return longest;
    }

    public static bool IsMaterial(PrecisionTerminalEvidence evidence) =>
        evidence.LongestRecoverableWorkingRun >= 8 ||
        evidence.WorkingQualityAvailable && evidence.WorkingP99DeltaE00 >= 1.0 ||
        evidence.PhaseZeroThresholdCrossed ||
        evidence.PlannedStageContractLoss;

    public static bool IsQualityMaterial(PrecisionOutputQuality quality) =>
        quality.Available && quality.P99DeltaE00 is >= 1.0;

    public static PrecisionCensusTerminal SelectTerminal(
        PrecisionTerminalEvidence first,
        PrecisionTerminalEvidence second)
    {
        if (!first.Valid || !second.Valid || first != second ||
            first.IndeterminateCouldBeMaterial)
        {
            return PrecisionCensusTerminal.Invalid;
        }
        if (IsMaterial(first))
        {
            return PrecisionCensusTerminal.Loss;
        }
        return first.WorkingQualityAvailable
            ? PrecisionCensusTerminal.Clean
            : PrecisionCensusTerminal.Invalid;
    }

    public static bool IsUseful(double value) =>
        value >= MinimumUseful && value <= MaximumUseful;
}

public sealed class PrecisionCensusLogicTests
{
    [Theory]
    [InlineData(-0.000001, PrecisionClipDirection.Negative)]
    [InlineData(0, PrecisionClipDirection.None)]
    [InlineData(1, PrecisionClipDirection.None)]
    [InlineData(1.000001, PrecisionClipDirection.AboveWhite)]
    public void ClassifyClip_UsesStrictStorageEdges(
        double reference,
        PrecisionClipDirection expected)
    {
        Assert.Equal(expected, PrecisionCensusLogic.ClassifyClip(reference));
    }

    [Fact]
    public void DetermineRecovery_PropagatesOnlyAboveWhiteThroughAnalyticStages()
    {
        Assert.Equal(
            PrecisionRecovery.ReturnsUseful,
            PrecisionCensusLogic.DetermineRecovery(
                PrecisionClipDirection.AboveWhite,
                remainingStagesAreAnalytic: true,
                finalReference: 0.5));
        Assert.Equal(
            PrecisionRecovery.DoesNotReturn,
            PrecisionCensusLogic.DetermineRecovery(
                PrecisionClipDirection.AboveWhite,
                remainingStagesAreAnalytic: true,
                finalReference: 1));
        Assert.Equal(
            PrecisionRecovery.Indeterminate,
            PrecisionCensusLogic.DetermineRecovery(
                PrecisionClipDirection.Negative,
                remainingStagesAreAnalytic: true,
                finalReference: 0.5));
        Assert.Equal(
            PrecisionRecovery.Indeterminate,
            PrecisionCensusLogic.DetermineRecovery(
                PrecisionClipDirection.AboveWhite,
                remainingStagesAreAnalytic: false,
                finalReference: null));
    }

    [Fact]
    public void LongestContiguousRun_DoesNotJoinSeparatedSamples()
    {
        var actual = PrecisionCensusLogic.LongestContiguousRun(
            [true, true, false, true, true, true, false]);

        Assert.Equal(3, actual);
    }

    [Fact]
    public void Materiality_IncludesThresholdEdgesButExcludesIngress()
    {
        Assert.False(PrecisionCensusLogic.IsMaterial(Evidence(
            run: 7, deltaE: 0.999999,
            ingressRun: 20, ingressDeltaE: 2)));
        Assert.True(PrecisionCensusLogic.IsMaterial(Evidence(run: 8)));
        Assert.True(PrecisionCensusLogic.IsMaterial(Evidence(deltaE: 1)));
        Assert.True(PrecisionCensusLogic.IsMaterial(Evidence(phaseZero: true)));
        Assert.True(PrecisionCensusLogic.IsMaterial(Evidence(planned: true)));
    }

    [Fact]
    public void ZeroEligibleQuality_IsUnavailableAndCannotPassMateriality()
    {
        var quality = PrecisionBoundaryCensus.AnalyzeQuality(
            actual: [0, 0, 0, 1, 1, 1],
            reference: [0, 0, 0, 1, 1, 1],
            sweep: [0, 1],
            width: 2,
            height: 1,
            clipped: [true, true]);

        Assert.False(quality.Available);
        Assert.Equal(2, quality.CandidatePixels);
        Assert.Equal(0, quality.EligiblePixels);
        Assert.Null(quality.MeanDeltaE00);
        Assert.Null(quality.P99DeltaE00);
        Assert.Null(quality.MaximumDeltaE00);
        Assert.False(PrecisionCensusLogic.IsQualityMaterial(quality));
        var unavailable = Evidence(qualityAvailable: false);
        Assert.False(PrecisionCensusLogic.IsMaterial(unavailable));
        Assert.Equal(
            PrecisionCensusTerminal.Invalid,
            PrecisionCensusLogic.SelectTerminal(unavailable, unavailable));
    }

    [Fact]
    public void Selector_RequiresRepeatedDeterminateEvidence()
    {
        var clean = Evidence();
        var loss = Evidence(run: 8);
        var indeterminate = Evidence(indeterminate: true);

        Assert.Equal(
            PrecisionCensusTerminal.Clean,
            PrecisionCensusLogic.SelectTerminal(clean, clean));
        Assert.Equal(
            PrecisionCensusTerminal.Loss,
            PrecisionCensusLogic.SelectTerminal(loss, loss));
        Assert.Equal(
            PrecisionCensusTerminal.Invalid,
            PrecisionCensusLogic.SelectTerminal(clean, loss));
        Assert.Equal(
            PrecisionCensusTerminal.Invalid,
            PrecisionCensusLogic.SelectTerminal(indeterminate, indeterminate));
    }

    private static PrecisionTerminalEvidence Evidence(
        int run = 0,
        double deltaE = 0,
        bool phaseZero = false,
        bool planned = false,
        bool indeterminate = false,
        bool qualityAvailable = true,
        int ingressRun = 0,
        double ingressDeltaE = 0) =>
        new(
            Valid: true,
            qualityAvailable,
            run,
            deltaE,
            phaseZero,
            planned,
            indeterminate,
            ingressRun,
            ingressDeltaE);
}
