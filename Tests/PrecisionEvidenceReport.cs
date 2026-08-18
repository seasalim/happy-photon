using System.Globalization;
using System.Text;

namespace HappyPhoton.Tests;

internal static class PrecisionEvidenceReport
{
    public static void AppendQuality(
        StringBuilder payload,
        string caseName,
        string population,
        string boundary,
        PrecisionOutputQuality quality,
        bool phaseZeroThresholdCrossed,
        bool plannedStageContractLoss,
        bool indeterminateCouldBeMaterial = false,
        PrecisionMetricState clipState = PrecisionMetricState.Available,
        PrecisionMetricState recoveryState = PrecisionMetricState.Available)
    {
        payload.Append("CENSUS_EVIDENCE case=").Append(caseName)
            .Append(" population=").Append(population)
            .Append(" boundary=").Append(boundary)
            .Append(" oracle=analytic required=clip,recovery,quality")
            .Append(" clipState=").Append(Token(clipState))
            .Append(" recoveryState=").Append(Token(recoveryState))
            .Append(" qualityState=").Append(Token(quality.State))
            .Append(" qualityReason=").Append(QualityReason(quality))
            .Append(" storedChangeState=inapplicable")
            .Append(" candidatePixels=").Append(quality.CandidatePixels)
            .Append(" eligiblePixels=").Append(quality.EligiblePixels)
            .Append(" countBelow1=").Append(
                quality.Available ? Integer(quality.CountBelowMateriality) : "null")
            .Append(" materialityRank=").Append(
                quality.Available ? Integer(quality.MaterialityRank) : "null")
            .Append(" p99Material=").Append(
                quality.Available ? Boolean(quality.P99Material) : "null")
            .Append(" meanDeltaE00=").Append(Format(quality.MeanDeltaE00))
            .Append(" p99DeltaE00=").Append(Format(quality.P99DeltaE00))
            .Append(" maxDeltaE00=").Append(Format(quality.MaximumDeltaE00))
            .Append(" decisionBasis=").Append(Token(quality.DecisionBasis))
            .Append(" percentileBasis=").Append(Token(quality.PercentileBasis))
            .Append(" retainedErrors=").Append(quality.RetainedErrors)
            .Append(" retentionStride=").Append(quality.RetentionStride)
            .Append(" longestRecoverableRun=0")
            .Append(" phaseZeroThresholdCrossed=")
            .Append(Boolean(phaseZeroThresholdCrossed))
            .Append(" plannedStageContractLoss=")
            .Append(Boolean(plannedStageContractLoss))
            .Append(" indeterminateCouldBeMaterial=")
            .Append(Boolean(indeterminateCouldBeMaterial))
            .AppendLine();
    }

    public static void AppendBoundary(
        StringBuilder payload,
        string caseName,
        string population,
        PrecisionBoundaryCapture boundary,
        PrecisionOutputQuality? quality = null,
        bool phaseZeroThresholdCrossed = false,
        bool plannedStageContractLoss = false)
    {
        if (boundary.Oracle == PrecisionBoundaryOracle.NotExecuted)
        {
            payload.Append("CENSUS_EVIDENCE case=").Append(caseName)
                .Append(" population=").Append(population)
                .Append(" boundary=").Append(boundary.Name)
                .Append(" oracle=not-executed required=none")
                .AppendLine();
            return;
        }
        if (boundary.Oracle == PrecisionBoundaryOracle.NativeOperator)
        {
            payload.Append("CENSUS_EVIDENCE case=").Append(caseName)
                .Append(" population=").Append(population)
                .Append(" boundary=").Append(boundary.Name)
                .Append(" oracle=native-operator required=stored-change")
                .Append(" clipState=inapplicable recoveryState=inapplicable")
                .Append(" qualityState=inapplicable storedChangeState=")
                .Append(Token(boundary.StoredChange.State))
                .Append(" comparedSamples=").Append(
                    boundary.StoredChange.ComparedSamples)
                .Append(" changedSamples=").Append(
                    boundary.StoredChange.ChangedSamples)
                .Append(" maxCodeChange=").Append(
                    boundary.StoredChange.MaximumCodeChange)
                .Append(" dimensionsChanged=").Append(Boolean(
                    boundary.StoredChange.DimensionsChanged))
                .Append(" basis=").Append(Token(boundary.StoredChange.Basis))
                .AppendLine();
            return;
        }

        var aggregate = boundary.Aggregates
            .Aggregate(new BoundaryTotals(), (total, row) => total.Add(row));
        var resolvedQuality = quality ?? PrecisionOutputQuality.Unavailable(
            checked(boundary.Width * boundary.Height));
        var qualityFullyClipped = resolvedQuality.State ==
                PrecisionMetricState.Inapplicable ||
            resolvedQuality.State == PrecisionMetricState.Unavailable &&
            resolvedQuality.EligiblePixels == 0 &&
            aggregate.NegativeClips + aggregate.AboveWhiteClips > 0;
        var qualityState = qualityFullyClipped
            ? PrecisionMetricState.Inapplicable
            : resolvedQuality.State;
        var qualityReason = qualityFullyClipped
            ? "fully-clipped-no-unclipped-pixels"
            : QualityReason(resolvedQuality);
        payload.Append("CENSUS_EVIDENCE case=").Append(caseName)
            .Append(" population=").Append(population)
            .Append(" boundary=").Append(boundary.Name)
            .Append(" oracle=analytic required=clip,recovery,quality")
            .Append(" clipState=").Append(Token(aggregate.ClipState))
            .Append(" recoveryState=").Append(Token(aggregate.RecoveryState))
            .Append(" qualityState=").Append(Token(qualityState))
            .Append(" qualityReason=").Append(qualityReason)
            .Append(" storedChangeState=inapplicable")
            .Append(" channelSamples=").Append(aggregate.ChannelSamples)
            .Append(" negativeClips=").Append(aggregate.NegativeClips)
            .Append(" aboveWhiteClips=").Append(aggregate.AboveWhiteClips)
            .Append(" recoverable=").Append(aggregate.Recoverable)
            .Append(" indeterminate=").Append(aggregate.Indeterminate)
            .Append(" longestRecoverableRun=").Append(
                aggregate.LongestRecoverableRun)
            .Append(" eligiblePixels=").Append(resolvedQuality.EligiblePixels)
            .Append(" countBelow1=").Append(
                resolvedQuality.Available
                    ? Integer(resolvedQuality.CountBelowMateriality)
                    : "null")
            .Append(" materialityRank=").Append(
                resolvedQuality.Available
                    ? Integer(resolvedQuality.MaterialityRank)
                    : "null")
            .Append(" p99Material=").Append(
                resolvedQuality.Available
                    ? Boolean(resolvedQuality.P99Material)
                    : "null")
            .Append(" decisionBasis=").Append(Token(resolvedQuality.DecisionBasis))
            .Append(" phaseZeroThresholdCrossed=")
            .Append(Boolean(phaseZeroThresholdCrossed))
            .Append(" plannedStageContractLoss=")
            .Append(Boolean(plannedStageContractLoss))
            .Append(" indeterminateCouldBeMaterial=")
            .Append(Boolean(aggregate.Indeterminate > 0))
            .AppendLine();
    }

    private static string Token<T>(T value) where T : struct, Enum =>
        value.ToString().ToLowerInvariant().Replace(
            "exactfullpopulation", "exact-full-population", StringComparison.Ordinal)
        .Replace(
            "descriptivesystematicsample", "descriptive-systematic-sample",
            StringComparison.Ordinal)
        .Replace("notapplicable", "not-applicable", StringComparison.Ordinal);
    private static string Format(double? value) => value?.ToString(
        "F9", CultureInfo.InvariantCulture) ?? "null";
    private static string QualityReason(PrecisionOutputQuality quality) =>
        quality.Available
            ? "measured"
            : quality.InapplicableReason ?? "measurement-unavailable";
    private static string Integer(int value) => value.ToString(
        CultureInfo.InvariantCulture);
    private static string Boolean(bool value) => value ? "true" : "false";

    private sealed record BoundaryTotals(
        PrecisionMetricState ClipState = PrecisionMetricState.Available,
        PrecisionMetricState RecoveryState = PrecisionMetricState.Available,
        int ChannelSamples = 0,
        int NegativeClips = 0,
        int AboveWhiteClips = 0,
        int Recoverable = 0,
        int Indeterminate = 0,
        int LongestRecoverableRun = 0)
    {
        public BoundaryTotals Add(PrecisionBoundaryAggregate value) => this with
        {
            ClipState = value.ClipState,
            RecoveryState = value.RecoveryState,
            ChannelSamples = ChannelSamples + value.ChannelSamples,
            NegativeClips = NegativeClips + value.NegativeClips,
            AboveWhiteClips = AboveWhiteClips + value.AboveWhiteClips,
            Recoverable = Recoverable + value.Recoverable,
            Indeterminate = Indeterminate + value.Indeterminate,
            LongestRecoverableRun = Math.Max(
                LongestRecoverableRun,
                value.LongestRecoverableRun)
        };
    }
}
