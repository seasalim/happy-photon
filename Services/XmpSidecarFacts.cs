using HappyPhoton.Models;

namespace HappyPhoton.Services;

public enum XmpSidecarMode
{
    Off,
    Read,
    ReadWrite
}

public enum XmpSidecarNaming
{
    FullName,
    BaseName
}

[Flags]
public enum AssessmentAxes
{
    None = 0,
    Rating = 1,
    Flag = 2,
    Label = 4,
    All = Rating | Flag | Label
}

public enum XmpFactKind
{
    Missing,
    Matched,
    Empty,
    Unsupported,
    WeakClear
}

public readonly record struct XmpFact<T>(XmpFactKind Kind, T Value)
{
    public static XmpFact<T> Missing => new(XmpFactKind.Missing, default!);
    public static XmpFact<T> Empty => new(XmpFactKind.Empty, default!);
    public static XmpFact<T> Unsupported => new(XmpFactKind.Unsupported, default!);
    public static XmpFact<T> WeakClear(T value) => new(XmpFactKind.WeakClear, value);
    public static XmpFact<T> Matched(T value) => new(XmpFactKind.Matched, value);
    public bool CanAdopt => Kind is XmpFactKind.Matched or XmpFactKind.Empty;
}

public sealed record XmpSidecarFacts(
    XmpFact<int> Rating,
    XmpFact<ImageFlag> Flag,
    XmpFact<ColorLabel> Label);

public sealed record AssessmentSnapshot(
    long ImageId,
    string FilePath,
    ImageFlag Flag,
    int Rating,
    ColorLabel ColorLabel,
    long Revision,
    DateTime AssessedUtc,
    AssessmentAxes PendingAxes);

public sealed record AssessmentMutation(
    long ImageId,
    AssessmentAxes Axes,
    ImageFlag? Flag = null,
    int? Rating = null,
    ColorLabel? ColorLabel = null);

public sealed record XmpReconcileItem(
    AssessmentSnapshot Snapshot,
    XmpSidecarCandidate Sidecar,
    XmpSidecarFacts Facts);

public sealed record XmpReconcileAdoption(
    AssessmentSnapshot Snapshot,
    AssessmentAxes AdoptedAxes);
