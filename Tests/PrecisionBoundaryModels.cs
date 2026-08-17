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
    PrecisionClipDirection Clip,
    PrecisionRecovery Recovery);

internal sealed record PrecisionBoundaryCapture(
    string Name,
    PrecisionBoundaryScope Scope,
    PrecisionBoundaryOracle Oracle,
    bool Executed,
    int Width,
    int Height,
    ushort[] InputStoredQ16,
    IReadOnlyList<PrecisionBoundarySample> Samples);

internal sealed record PrecisionOutputQuality(
    bool Available,
    int CandidatePixels,
    int EligiblePixels,
    double? MeanDeltaE00,
    double? P99DeltaE00,
    double? MaximumDeltaE00)
{
    public double EligibleFraction => CandidatePixels == 0
        ? 0
        : EligiblePixels / (double)CandidatePixels;

    public static PrecisionOutputQuality Unavailable(int candidatePixels) =>
        new(false, candidatePixels, 0, null, null, null);
}

internal sealed record PrecisionCensusCapture(
    IReadOnlyList<PrecisionBoundaryCapture> Boundaries,
    PrecisionOutputQuality IngressQuality,
    PrecisionOutputQuality WorkingStorageQuality,
    double Fold,
    double NormalizedCubeMaximum,
    IReadOnlyList<string> GateFailures);
