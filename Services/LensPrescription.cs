namespace HappyPhoton.Services;

internal enum LensPrescriptionStatus
{
    None,
    Available,
    Unsupported,
    Invalid
}

internal enum LensPrescriptionSource
{
    DngOpcode,
    FujifilmMakerNote
}

internal sealed record LensPrescriptionReadResult(
    LensPrescription? Prescription,
    LensPrescriptionStatus Status,
    string? Message)
{
    internal static LensPrescriptionReadResult None { get; } =
        new(null, LensPrescriptionStatus.None, null);

    internal static LensPrescriptionReadResult Available(
        LensPrescription prescription) =>
        new(prescription, LensPrescriptionStatus.Available, null);

    internal static LensPrescriptionReadResult Reject(
        LensPrescriptionStatus status,
        string message) => new(null, status, message);
}

internal sealed record LensPrescription(
    LensPrescriptionSource Source,
    string? LensName,
    IReadOnlyList<LensWarp> Warps,
    IReadOnlyList<LensVignette> Vignettes,
    LensFrameWindow SourceWindow,
    LensFrameWindow OutputWindow,
    FujiLensTables? FujiTables = null)
{
    // Green (or the sole plane) is always the shared distortion reference;
    // distinct red/blue planes additionally advertise lateral CA.
    internal bool HasDistortion => Warps.Count > 0;
    internal bool HasChromaticAberration => Warps.Any(warp => warp.HasPerPlaneGeometry);
    internal bool HasVignetting => Vignettes.Count > 0;

    internal LensPrescriptionSummary Summary => new(
        LensName,
        Source == LensPrescriptionSource.DngOpcode
            ? "DNG OPCODES"
            : "FUJIFILM MAKER NOTE",
        HasDistortion,
        HasChromaticAberration,
        HasVignetting);
}

public sealed record LensPrescriptionSummary(
    string? LensName,
    string Source,
    bool HasDistortion,
    bool HasChromaticAberration,
    bool HasVignetting)
{
    public bool HasAny => HasDistortion || HasChromaticAberration || HasVignetting;
}

internal readonly record struct LensFrameWindow(
    double Left,
    double Top,
    double Right,
    double Bottom)
{
    internal static LensFrameWindow Full { get; } = new(0, 0, 1, 1);
    internal double Width => Right - Left;
    internal double Height => Bottom - Top;
    internal bool IsValid => double.IsFinite(Left) && double.IsFinite(Top) &&
        double.IsFinite(Right) && double.IsFinite(Bottom) &&
        Left >= 0 && Top >= 0 && Right > Left && Bottom > Top &&
        Right <= 1 && Bottom <= 1;
}

internal sealed record LensWarp(
    IReadOnlyList<LensWarpCoefficients> Planes,
    double CenterX,
    double CenterY)
{
    internal bool HasPerPlaneGeometry => Planes.Count == 3 &&
        (Planes[0] != Planes[1] || Planes[1] != Planes[2]);
}

internal readonly record struct LensWarpCoefficients(
    double Kr0,
    double Kr1,
    double Kr2,
    double Kr3,
    double Kt0,
    double Kt1)
{
    internal bool IsFinite => double.IsFinite(Kr0) && double.IsFinite(Kr1) &&
        double.IsFinite(Kr2) && double.IsFinite(Kr3) &&
        double.IsFinite(Kt0) && double.IsFinite(Kt1);
}

internal sealed record LensVignette(
    double K0,
    double K1,
    double K2,
    double K3,
    double K4,
    double CenterX,
    double CenterY)
{
    internal bool IsFinite => new[] { K0, K1, K2, K3, K4, CenterX, CenterY }
        .All(double.IsFinite);
}

internal sealed record FujiLensTables(
    LensRadialTable Distortion,
    LensChromaticAberrationTable ChromaticAberration,
    LensRadialTable Vignetting);

internal sealed record LensRadialTable(
    double Scale,
    IReadOnlyList<double> Radii,
    IReadOnlyList<double> Values);

internal sealed record LensChromaticAberrationTable(
    double Scale,
    IReadOnlyList<double> Radii,
    IReadOnlyList<double> Red,
    IReadOnlyList<double> Blue);
