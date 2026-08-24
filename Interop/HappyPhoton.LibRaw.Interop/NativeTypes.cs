using System.Runtime.InteropServices;

namespace HappyPhoton.LibRaw.Interop;

internal enum NativeStatus
{
    Ok = 0,
    Absent = 1,
    Unavailable = 2
}

internal enum NativeErrorClass
{
    None = 0,
    LibRaw = 1,
    Abi = 2,
    Programming = 3,
    Bridge = 4
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal unsafe struct NativeError
{
    internal uint AbiVersion;
    internal uint StructSize;
    internal int ErrorClass;
    internal int NativeCode;
    internal byte* Text;
    internal uint TextCapacity;
    internal uint TextLength;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal unsafe struct NativeRuntimeInfo
{
    internal uint AbiVersion;
    internal uint StructSize;
    internal uint LibRawVersionNumber;
    internal uint Capabilities;
    internal uint ThreadSafeVariant;
    internal uint VersionStringLength;
    internal fixed byte VersionString[128];
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct NativeDimensions
{
    internal uint AbiVersion, StructSize;
    internal uint RawWidth, RawHeight, VisibleWidth, VisibleHeight, OutputWidth, OutputHeight;
    internal int Orientation;
    internal uint Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal unsafe struct NativeSensorIdentity
{
    internal uint AbiVersion, StructSize;
    internal int Colors;
    internal uint Filters, DngVersion, XtransCount;
    internal fixed sbyte Xtrans[36];
    internal uint CdescLength;
    internal fixed byte Cdesc[5];
    internal fixed byte Reserved[3];
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct NativeGpsFacts
{
    internal uint Parsed, CoordinatePresent;
    internal double Latitude, Longitude;
    internal uint AltitudePresent;
    internal float Altitude;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal unsafe struct NativeMetadata
{
    internal uint AbiVersion, StructSize;
    internal uint MakeLength;
    internal fixed byte Make[128];
    internal uint ModelLength;
    internal fixed byte Model[128];
    internal uint NormalizedMakeLength;
    internal fixed byte NormalizedMake[128];
    internal uint NormalizedModelLength;
    internal fixed byte NormalizedModel[128];
    internal uint LensLength;
    internal fixed byte Lens[128];
    internal uint IsoPresent;
    internal float Iso;
    internal uint ShutterPresent;
    internal float Shutter;
    internal uint AperturePresent;
    internal float Aperture;
    internal uint FocalLengthPresent;
    internal float FocalLength;
    internal uint FocalLength35mmPresent;
    internal float FocalLength35mm;
    internal uint TimestampPresent;
    internal long Timestamp;
    internal int Orientation;
    internal uint Reserved;
    internal NativeGpsFacts Gps;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal unsafe struct NativeCameraFacts
{
    internal uint AbiVersion, StructSize, MultiplierCount;
    internal fixed float Multipliers[4];
    internal uint MatrixRows, MatrixColumns;
    internal fixed float CameraToSrgb[12];
    internal uint PreMultiplierCount;
    internal fixed float PreMultipliers[4];
    internal uint CameraFromXyzRows, CameraFromXyzColumns;
    internal fixed float CameraFromXyz[12];
    internal uint LinearMaxCount;
    internal fixed uint LinearMax[4];
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct NativeFujiFacts
{
    internal uint AbiVersion, StructSize, Present;
    internal float ExposureMidpointShift;
    internal uint DynamicRange, DynamicRangeSetting, DevelopmentDynamicRange, AutoDynamicRange;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal unsafe struct NativeLensIdentity
{
    internal uint AbiVersion, StructSize, Present, Reserved;
    internal ulong LensId, CameraId, TeleconverterId, AdapterId, AttachmentId;
    internal uint LensFormat, LensMount, CameraFormat, CameraMount;
    internal int FocalType;
    internal uint FocalUnits;
    internal float MinFocal, MaxFocal;
    internal float MaxApertureAtMinFocal, MaxApertureAtMaxFocal;
    internal float MinApertureAtMinFocal, MinApertureAtMaxFocal;
    internal float MaxAperture, MinAperture, CurrentFocal, CurrentAperture;
    internal float MaxApertureAtCurrentFocal, MinApertureAtCurrentFocal;
    internal float MinFocusDistance, FocusRangeIndex, LensFStops, FocalLength35mm;
    internal uint LensLength;
    internal fixed byte Lens[128];
    internal uint TeleconverterLength;
    internal fixed byte Teleconverter[128];
    internal uint AdapterLength;
    internal fixed byte Adapter[128];
    internal uint AttachmentLength;
    internal fixed byte Attachment[128];
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal unsafe struct NativeOutputConfig
{
    internal uint AbiVersion, StructSize;
    internal int OutputBits, OutputColor;
    internal double GammaPower, GammaSlope;
    internal int NoAutoBright, HalfSize, HighlightMode, FbddNoiseReduction;
    internal int UseCameraWhiteBalance, UseAutoWhiteBalance;
    internal fixed float UserMultipliers[4];
    internal int UseCameraMatrix, Reserved;
    internal int UserSaturation;
    internal uint UserQualityPresent;
    internal int UserQuality;
    internal uint CropBoxPresent;
    internal fixed uint CropBox[4];
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal unsafe struct NativeImageDescriptor
{
    internal uint AbiVersion, StructSize;
    internal byte* Data;
    internal ulong ByteLength;
    internal uint Width, Height, BitsPerSample, Channels;
    internal int Format;
    internal uint Reserved;
    internal ulong Allocation;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal unsafe struct NativeMosaicDescriptor
{
    internal uint AbiVersion, StructSize;
    internal ushort* Data;
    internal ulong ByteLength;
    internal uint RawPitch, RawWidth, RawHeight, VisibleWidth, VisibleHeight;
    internal uint TopMargin, LeftMargin, Black, Maximum, CblackCount;
    internal uint RepeatingRows, RepeatingColumns;
    internal fixed uint Cblack[4104];
    internal ulong Lease;
}
