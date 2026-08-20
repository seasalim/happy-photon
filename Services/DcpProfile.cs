using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal enum DcpProfileErrorCode
{
    None,
    Missing,
    Unavailable,
    TooLarge,
    Corrupt,
    InvalidContainer,
    MissingMandatoryTag,
    UnknownIlluminant,
    InvalidDimensions,
    UnsupportedVariant,
    SignatureMismatch,
    HashMismatch,
    MissingWhiteBalance
}

internal sealed class DcpProfileException : IOException
{
    internal DcpProfileErrorCode Code { get; }

    internal DcpProfileException(DcpProfileErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }
}

internal sealed record DcpHueSatMap(
    int HueDivisions,
    int SaturationDivisions,
    int ValueDivisions,
    bool EncodeValueAsSrgb,
    float[] Table1,
    float[]? Table2,
    double IlluminantWeight)
{
    internal ushort[]? RgbLut { get; init; }
}

internal sealed record DcpProfile(
    string Name,
    string? UniqueCameraModel,
    double[,] ColorMatrix1,
    double[,]? ColorMatrix2,
    double[,]? ForwardMatrix1,
    double[,]? ForwardMatrix2,
    int CalibrationIlluminant1,
    int? CalibrationIlluminant2,
    string CalibrationSignature,
    int EmbedPolicy,
    int HueDivisions,
    int SaturationDivisions,
    int ValueDivisions,
    bool EncodeValueAsSrgb,
    float[]? HueSatTable1,
    float[]? HueSatTable2,
    string ContentHash)
{
    private const int MaximumCachedLuts = 16;

    // Process-wide: building the 274K-entry RGB LUT costs ~40 ms, and a
    // freshly parsed instance of the same profile content (cold selection,
    // re-parse after invalidation) must not pay it again. Content hash +
    // interpolation weight identify the table exactly.
    private static readonly object LutSync = new();
    private static readonly Dictionary<(string Hash, long Weight), ushort[]>
        RgbLuts = [];

    internal ushort[] GetOrCreateRgbLut(
        double weight,
        Func<ushort[]> create)
    {
        var key = (
            ContentHash,
            HueSatTable2 == null ? 0 : BitConverter.DoubleToInt64Bits(weight));
        lock (LutSync)
        {
            if (RgbLuts.TryGetValue(key, out var cached)) return cached;
            if (RgbLuts.Count >= MaximumCachedLuts) RgbLuts.Clear();
            var result = create();
            RgbLuts[key] = result;
            return result;
        }
    }
}

internal sealed record DcpCameraData(
    double[]? AnalogBalance,
    double[,]? CameraCalibration1,
    double[,]? CameraCalibration2,
    double[,]? ReductionMatrix1,
    double[,]? ReductionMatrix2,
    double[]? AsShotNeutral,
    string CalibrationSignature)
{
    internal static DcpCameraData Defaults { get; } = new(
        null, null, null, null, null, null, string.Empty);
}

internal sealed record DcpProfileResolution(
    RawProfileSelection? Selection,
    DcpProfile? Profile,
    DcpProfileErrorCode Status,
    string Token,
    string? Message)
{
    internal bool IsActive => Profile != null && Status == DcpProfileErrorCode.None;

    internal static DcpProfileResolution BuiltIn { get; } = new(
        null, null, DcpProfileErrorCode.None, string.Empty, null);

    internal static DcpProfileResolution Success(
        RawProfileSelection selection,
        DcpProfile profile) => new(
            selection,
            profile,
            DcpProfileErrorCode.None,
            selection.CacheToken,
            null);

    internal static DcpProfileResolution Rejected(
        RawProfileSelection selection,
        DcpProfileErrorCode status,
        string message,
        string? observedHash = null) => new(
            selection,
            null,
            status,
            $"{selection.CacheToken}:{StatusKey(status)}" +
            (observedHash == null ? string.Empty : $":{observedHash}"),
            message);

    private static string StatusKey(DcpProfileErrorCode status) => status switch
    {
        DcpProfileErrorCode.Missing => "missing",
        DcpProfileErrorCode.Unavailable => "unavailable",
        DcpProfileErrorCode.TooLarge => "too-large",
        DcpProfileErrorCode.Corrupt => "corrupt",
        DcpProfileErrorCode.InvalidContainer => "invalid-container",
        DcpProfileErrorCode.MissingMandatoryTag => "missing-tag",
        DcpProfileErrorCode.UnknownIlluminant => "unknown-illuminant",
        DcpProfileErrorCode.InvalidDimensions => "bad-dimensions",
        DcpProfileErrorCode.UnsupportedVariant => "unsupported",
        DcpProfileErrorCode.SignatureMismatch => "signature-mismatch",
        DcpProfileErrorCode.HashMismatch => "hash-mismatch",
        DcpProfileErrorCode.MissingWhiteBalance => "missing-wb",
        _ => "rejected"
    };
}

internal sealed record DcpProfilePayload(
    string Token,
    string Name,
    DcpHueSatMap? HueSatMap);

internal sealed record CameraIdentity(string? Make, string? Model)
{
    internal string Normalized => DcpProfileDiscovery.NormalizeCameraIdentity(
        Make,
        Model);
}
