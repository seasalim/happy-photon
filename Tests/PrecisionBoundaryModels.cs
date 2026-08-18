namespace HappyPhoton.Tests;

internal enum PrecisionBoundaryScope
{
    Ingress,
    WorkingStorage,
    Output
}

internal enum PrecisionBoundaryOracle
{
    Analytic,
    NativeOperator,
    NotExecuted
}

internal enum PrecisionMetricState
{
    Available,
    Inapplicable,
    Unavailable
}

internal enum PrecisionMetricBasis
{
    ExactFullPopulation,
    DescriptiveSystematicSample,
    NotApplicable
}

public enum PrecisionClipDirection
{
    None,
    Negative,
    AboveWhite
}

public enum PrecisionRecovery
{
    NotApplicable,
    ReturnsUseful,
    DoesNotReturn,
    Indeterminate
}

internal sealed record PrecisionBoundarySample(
    int X,
    int Y,
    int Channel,
    double? UnclampedReference,
    int StoredCode,
    int StoredMaximum,
    PrecisionClipDirection? Clip,
    PrecisionRecovery? Recovery);

internal sealed record PrecisionBoundaryAggregate(
    PrecisionMetricState ClipState,
    PrecisionMetricState RecoveryState,
    PrecisionMetricBasis Basis,
    int ChannelSamples,
    int NegativeClips,
    int AboveWhiteClips,
    int Recoverable,
    int Indeterminate,
    int LongestRecoverableRun,
    double? MaximumNegativeExcursion,
    double? MaximumAboveWhiteExcursion);

internal sealed record PrecisionStoredChange(
    PrecisionMetricState State,
    PrecisionMetricBasis Basis,
    int ComparedSamples,
    int ChangedSamples,
    int MaximumCodeChange,
    bool DimensionsChanged);

internal sealed record PrecisionBoundaryCapture(
    string Name,
    PrecisionBoundaryScope Scope,
    PrecisionBoundaryOracle Oracle,
    bool Executed,
    int Width,
    int Height,
    ushort[] InputStoredQ16,
    IReadOnlyList<PrecisionBoundarySample> Samples,
    IReadOnlyList<PrecisionBoundaryAggregate> Aggregates,
    PrecisionStoredChange StoredChange,
    int RetentionStride);

internal sealed record PrecisionOutputQuality(
    bool Available,
    int CandidatePixels,
    int EligiblePixels,
    double? MeanDeltaE00,
    double? P99DeltaE00,
    double? MaximumDeltaE00,
    int CountBelowMateriality = 0,
    int MaterialityRank = 0,
    bool P99Material = false,
    PrecisionMetricBasis DecisionBasis = PrecisionMetricBasis.ExactFullPopulation,
    PrecisionMetricBasis PercentileBasis = PrecisionMetricBasis.ExactFullPopulation,
    int RetainedErrors = 0,
    int RetentionStride = 1,
    string? InapplicableReason = null)
{
    public PrecisionMetricState State => Available
        ? PrecisionMetricState.Available
        : InapplicableReason == null
            ? PrecisionMetricState.Unavailable
            : PrecisionMetricState.Inapplicable;

    public double EligibleFraction => CandidatePixels == 0
        ? 0
        : EligiblePixels / (double)CandidatePixels;

    public static PrecisionOutputQuality Unavailable(int candidatePixels) =>
        new(false, candidatePixels, 0, null, null, null,
            DecisionBasis: PrecisionMetricBasis.NotApplicable,
            PercentileBasis: PrecisionMetricBasis.NotApplicable,
            RetentionStride: 0);

    public static PrecisionOutputQuality FullyClipped(int candidatePixels) =>
        new(false, candidatePixels, 0, null, null, null,
            DecisionBasis: PrecisionMetricBasis.NotApplicable,
            PercentileBasis: PrecisionMetricBasis.NotApplicable,
            RetentionStride: 0,
            InapplicableReason: "fully-clipped-no-unclipped-pixels");
}

internal sealed record PrecisionCensusCapture(
    IReadOnlyList<PrecisionBoundaryCapture> Boundaries,
    PrecisionOutputQuality IngressQuality,
    PrecisionOutputQuality WorkingStorageQuality,
    double Fold,
    double NormalizedCubeMaximum,
    IReadOnlyList<string> GateFailures);
