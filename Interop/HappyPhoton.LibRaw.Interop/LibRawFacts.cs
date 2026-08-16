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

public sealed record LibRawCameraFacts(float[] Multipliers, float[,] CameraToSrgb);

public sealed record LibRawFujiFacts(
    float ExposureMidpointShift,
    uint DynamicRange,
    uint DynamicRangeSetting,
    uint DevelopmentDynamicRange,
    uint AutoDynamicRange);
