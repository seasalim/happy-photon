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
    FujifilmMakerNote,
    Lensfun
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
    FujiLensTables? FujiTables = null,
    IReadOnlyList<LensTableWarp>? TableWarps = null,
    IReadOnlyList<LensTableVignette>? TableVignettes = null,
    LensfunDistortion? LensfunDistortion = null,
    LensfunTca? LensfunTca = null,
    LensfunVignette? LensfunVignette = null,
    LensClassSources? ClassSources = null)
{
    internal IReadOnlyList<LensTableWarp> RadialTableWarps => TableWarps ?? [];
    internal IReadOnlyList<LensTableVignette> RadialTableVignettes => TableVignettes ?? [];

    // Green (or the sole plane) is always the shared distortion reference;
    // distinct red/blue planes additionally advertise lateral CA.
    internal bool HasDistortion => Warps.Count > 0 || LensfunDistortion != null ||
        RadialTableWarps.Any(warp => warp.Distortion != null);
    internal bool HasChromaticAberration =>
        Warps.Any(warp => warp.HasPerPlaneGeometry) ||
        LensfunTca != null ||
        RadialTableWarps.Any(warp => warp.ChromaticAberration != null);
    internal bool HasVignetting => Vignettes.Count > 0 ||
        RadialTableVignettes.Count > 0 || LensfunVignette != null;

    internal LensPrescriptionSummary Summary => GetSummary(null);

    internal LensPrescriptionSummary GetSummary(BaseDecodeSettings? settings) => new(
        LensName,
        string.Join(" + ", ActiveSources(settings)
            .Distinct()
            .Select(SourceName)),
        HasDistortion,
        HasChromaticAberration,
        HasVignetting);

    private IEnumerable<LensPrescriptionSource> ActiveSources(
        BaseDecodeSettings? settings)
    {
        var sources = ClassSources ?? new LensClassSources(
            HasDistortion ? Source : null,
            HasChromaticAberration ? Source : null,
            HasVignetting ? Source : null);
        if ((settings?.Distortion ?? true) && sources.Distortion is { } distortion)
            yield return distortion;
        if ((settings?.ChromaticAberration ?? true) &&
            sources.ChromaticAberration is { } tca)
            yield return tca;
        if ((settings?.Vignetting ?? true) && sources.Vignetting is { } vignette)
            yield return vignette;
    }

    internal static string SourceName(LensPrescriptionSource source) => source switch
    {
        LensPrescriptionSource.DngOpcode => "DNG OPCODES",
        LensPrescriptionSource.FujifilmMakerNote => "FUJIFILM MAKER NOTE",
        LensPrescriptionSource.Lensfun => "LENSFUN",
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };
}

internal sealed record LensClassSources(
    LensPrescriptionSource? Distortion,
    LensPrescriptionSource? ChromaticAberration,
    LensPrescriptionSource? Vignetting);

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
    string Layout,
    LensRadialTable? Distortion,
    LensChromaticAberrationTable? ChromaticAberration,
    LensRadialTable? Vignetting,
    string? DistortionMessage = null,
    string? ChromaticAberrationMessage = null,
    string? VignettingMessage = null);

internal sealed record LensRadialTable(
    double Scale,
    IReadOnlyList<double> Radii,
    IReadOnlyList<double> Values,
    double NativePixelsPerRadiusUnit = 1,
    double ValueScale = 1);

internal sealed record LensChromaticAberrationTable(
    double Scale,
    IReadOnlyList<double> Radii,
    IReadOnlyList<double> Red,
    IReadOnlyList<double> Blue,
    double NativePixelsPerRadiusUnit = 1);

internal sealed record LensTableWarp(
    LensRadialTable? Distortion,
    LensChromaticAberrationTable? ChromaticAberration,
    double CenterX = 0.5,
    double CenterY = 0.5);

internal sealed record LensTableVignette(
    LensRadialTable Table,
    double CenterX = 0.5,
    double CenterY = 0.5);

internal enum LensfunDistortionModel { Poly3, Poly5, Ptlens }
internal enum LensfunTcaModel { Linear, Poly3 }

internal sealed record LensfunDistortion(
    LensfunDistortionModel Model,
    IReadOnlyList<double> Coefficients,
    double RadiusScale,
    double CenterX,
    double CenterY);

internal sealed record LensfunTca(
    LensfunTcaModel Model,
    IReadOnlyList<double> Red,
    IReadOnlyList<double> Blue,
    double RadiusScale,
    double CenterX,
    double CenterY);

internal sealed record LensfunVignette(
    double K1,
    double K2,
    double K3,
    double RadiusScale,
    double CenterX,
    double CenterY);
