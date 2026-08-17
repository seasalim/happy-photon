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

/// <summary>A native-independent, versioned description of LibRaw output.</summary>
public readonly record struct LibRawOutputConfiguration
{
    public const uint Version = 2;

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
            FbddNoiseReduction is < 0 or > 2)
            throw new ArgumentException("Output configuration contains an invalid value.");
    }
}
