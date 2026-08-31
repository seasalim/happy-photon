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
    Crop = 8,
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
    XmpFact<ColorLabel> Label,
    XmpFact<CropRegion> Crop);

public enum XmpCropProjectionKind
{
    None,
    Portable,
    NotPortable
}

public readonly record struct XmpCropProjection(
    XmpCropProjectionKind Kind,
    CropRegion? Crop,
    string? Reason = null)
{
    public static XmpCropProjection From(EditSettings settings)
    {
        if (settings.Rotation != 0 || settings.HorizonRotation != 0 ||
            settings.Geometry?.IsIdentity == false)
        {
            return new(XmpCropProjectionKind.NotPortable, null,
                "frame-changing geometry is active");
        }
        return settings.Crop is { IsFullImage: false } crop
            ? new(XmpCropProjectionKind.Portable, crop.Clone())
            : new(XmpCropProjectionKind.None, null);
    }

    public static bool HasGeometryEdits(EditSettings settings) =>
        settings.Rotation != 0 || settings.HorizonRotation != 0 ||
        settings.Crop is { IsFullImage: false } ||
        settings.Geometry?.IsIdentity == false;

    public static bool GeometryChanged(EditSettings before, EditSettings after) =>
        before.Rotation != after.Rotation ||
        before.HorizonRotation != after.HorizonRotation ||
        !CropsMatch(before.Crop, after.Crop) ||
        !GeometryMatches(before.Geometry, after.Geometry);

    internal static bool CropsMatch(CropRegion? left, CropRegion? right) =>
        ReferenceEquals(left, right) ||
        left != null && right != null &&
        left.Left == right.Left && left.Top == right.Top &&
        left.Right == right.Right && left.Bottom == right.Bottom;

    private static bool GeometryMatches(
        GeometrySettings? left,
        GeometrySettings? right)
    {
        var leftActive = left?.IsIdentity == false;
        var rightActive = right?.IsIdentity == false;
        return leftActive == rightActive && (!leftActive ||
            left!.Vertical == right!.Vertical &&
            left.Horizontal == right.Horizontal &&
            left.Aspect == right.Aspect &&
            left.Distortion == right.Distortion);
    }
}

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
    ColorLabel? ColorLabel = null,
    AssessmentAxes PendingAxes = AssessmentAxes.None);

public sealed record XmpReconcileItem(
    AssessmentSnapshot Snapshot,
    XmpSidecarCandidate Sidecar,
    XmpSidecarFacts Facts);

public sealed record XmpReconcileAdoption(
    AssessmentSnapshot Snapshot,
    AssessmentAxes AdoptedAxes,
    CropRegion? AdoptedCrop = null);
