namespace HappyPhoton.LibRaw.Interop;

public enum LibRawHighlightMode
{
    Clip = 0,
    Blend = 2
}

public enum LibRawFbddMode
{
    Off = 0,
    Light = 1,
    Full = 2
}

/// <summary>A named LibRaw demosaic request; the sensor may select its documented fallback.</summary>
public enum LibRawDemosaicQuality
{
    Linear = 0,
    Vng = 1,
    Ppg = 2,
    Ahd = 3,
    Dcb = 4,
    Dht = 11,
    Aahd = 12
}

/// <summary>A full-resolution, pre-rotation region inside the visible RAW frame.</summary>
public readonly record struct LibRawCropBox(uint X, uint Y, uint Width, uint Height);

/// <summary>A native-independent, versioned description of LibRaw output.</summary>
public readonly record struct LibRawOutputConfiguration
{
    public const uint Version = 3;

    public uint AbiVersion { get; init; }
    public int OutputBits { get; init; }
    public int OutputColor { get; init; }
    public double GammaPower { get; init; }
    public double GammaSlope { get; init; }
    public bool NoAutoBright { get; init; }
    public bool HalfSize { get; init; }
    public int HighlightMode { get; init; }
    public int FbddNoiseReduction { get; init; }
    public bool UseCameraWhiteBalance { get; init; }
    public bool UseAutoWhiteBalance { get; init; }
    public float UserMultiplier0 { get; init; }
    public float UserMultiplier1 { get; init; }
    public float UserMultiplier2 { get; init; }
    public float UserMultiplier3 { get; init; }
    public bool UseCameraMatrix { get; init; }
    public int? UserSaturation { get; init; }
    public LibRawDemosaicQuality? UserQuality { get; init; }
    public LibRawCropBox? CropBox { get; init; }

    public static LibRawOutputConfiguration Linear(
        LibRawHighlightMode highlight,
        LibRawFbddMode noiseReduction,
        bool halfSize) => new()
        {
            AbiVersion = Version,
            OutputBits = 16,
            OutputColor = 1,
            GammaPower = 1,
            GammaSlope = 1,
            NoAutoBright = true,
            HalfSize = halfSize,
            HighlightMode = (int)highlight,
            FbddNoiseReduction = (int)noiseReduction,
            UseCameraWhiteBalance = true,
            UseAutoWhiteBalance = false,
            UserMultiplier0 = 0,
            UserMultiplier1 = 0,
            UserMultiplier2 = 0,
            UserMultiplier3 = 0,
            UseCameraMatrix = true
        };

    public static LibRawOutputConfiguration LinearRec2020(
        LibRawHighlightMode highlight,
        LibRawFbddMode noiseReduction,
        bool halfSize) => Linear(highlight, noiseReduction, halfSize) with
        {
            OutputColor = 8
        };

    public static LibRawOutputConfiguration LinearCameraNative(
        LibRawHighlightMode highlight,
        LibRawFbddMode noiseReduction,
        bool halfSize) => Linear(highlight, noiseReduction, halfSize) with
        {
            OutputColor = 0
        };

    public static LibRawOutputConfiguration FullDecodeSrgb() => new()
    {
        AbiVersion = Version,
        OutputBits = 8,
        OutputColor = 1,
        GammaPower = 1.0 / 2.4,
        GammaSlope = 12.92,
        NoAutoBright = false,
        HalfSize = false,
        HighlightMode = 0,
        FbddNoiseReduction = 0,
        UseCameraWhiteBalance = true,
        UseAutoWhiteBalance = false,
        UserMultiplier0 = 0,
        UserMultiplier1 = 0,
        UserMultiplier2 = 0,
        UserMultiplier3 = 0,
        UseCameraMatrix = true
    };

    internal void Validate()
    {
        if (AbiVersion != Version)
            throw new ArgumentException("Unsupported output configuration version.");
        if (OutputBits is not (8 or 16) || OutputColor is < 0 or > 8 ||
            GammaPower <= 0 || GammaSlope <= 0 || HighlightMode < 0 ||
            FbddNoiseReduction is < 0 or > 2 || UserSaturation is < 0 or > ushort.MaxValue ||
            (UserQuality is { } quality && !Enum.IsDefined(quality)) ||
            (CropBox is { } crop && (crop.Width == 0 || crop.Height == 0 ||
                crop.X > int.MaxValue || crop.Y > int.MaxValue ||
                crop.Width > int.MaxValue || crop.Height > int.MaxValue)))
            throw new ArgumentException("Output configuration contains an invalid value.");
    }
}
