namespace HappyPhoton.LibRaw.Interop;

public sealed record LibRawDimensions(
    uint RawWidth, uint RawHeight, uint VisibleWidth, uint VisibleHeight,
    uint OutputWidth, uint OutputHeight, int Orientation);

public sealed record LibRawSensorIdentity(
    int Colors, uint Filters, uint DngVersion, sbyte[] XTrans, string ColorDescription);

public sealed record LibRawGpsFacts(
    bool Parsed, double? Latitude, double? Longitude, float? Altitude);

public sealed record LibRawMetadata(
    string? Make,
    string? Model,
    string? NormalizedMake,
    string? NormalizedModel,
    string? Lens,
    float? Iso,
    float? Shutter,
    float? Aperture,
    float? FocalLength,
    float? FocalLength35mm,
    long? Timestamp,
    int Orientation,
    LibRawGpsFacts Gps);

public sealed record LibRawCameraFacts(
    float[] Multipliers,
    float[,] CameraToSrgb,
    float[]? PreMultipliers,
    float[,]? CameraFromXyz,
    uint[]? LinearMax);

public sealed record LibRawFujiFacts(
    float ExposureMidpointShift,
    uint DynamicRange,
    uint DynamicRangeSetting,
    uint DevelopmentDynamicRange,
    uint AutoDynamicRange);

public sealed record LibRawLensIdentity(
    ulong LensId,
    string? Lens,
    uint LensFormat,
    uint LensMount,
    ulong CameraId,
    uint CameraFormat,
    uint CameraMount,
    int FocalType,
    uint FocalUnits,
    float MinFocal,
    float MaxFocal,
    float MaxApertureAtMinFocal,
    float MaxApertureAtMaxFocal,
    float MinApertureAtMinFocal,
    float MinApertureAtMaxFocal,
    float MaxAperture,
    float MinAperture,
    float CurrentFocal,
    float CurrentAperture,
    float MaxApertureAtCurrentFocal,
    float MinApertureAtCurrentFocal,
    float MinFocusDistance,
    float FocusRangeIndex,
    float LensFStops,
    float FocalLength35mm,
    ulong TeleconverterId,
    string? Teleconverter,
    ulong AdapterId,
    string? Adapter,
    ulong AttachmentId,
    string? Attachment);
