using System.Text.Json;
using System.Text.Json.Serialization;

namespace HappyPhoton.Models;

public sealed class StrictCamelCaseEnumConverter<TEnum>
    : JsonStringEnumConverter<TEnum> where TEnum : struct, Enum
{
    public StrictCamelCaseEnumConverter()
        : base(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
    {
    }
}

public enum WbMode
{
    [JsonStringEnumMemberName("asShot")]
    AsShot,
    [JsonStringEnumMemberName("custom")]
    Custom,
    [JsonStringEnumMemberName("preset")]
    Preset,
    [JsonStringEnumMemberName("picked")]
    Picked
}

public enum HlReconstructionMode
{
    [JsonStringEnumMemberName("blend")]
    Blend,
    [JsonStringEnumMemberName("clip")]
    Clip
}

public enum FbddMode
{
    [JsonStringEnumMemberName("off")]
    Off,
    [JsonStringEnumMemberName("light")]
    Light,
    [JsonStringEnumMemberName("full")]
    Full
}

public enum GrainSize
{
    [JsonStringEnumMemberName("fine")]
    Fine,
    [JsonStringEnumMemberName("medium")]
    Medium,
    [JsonStringEnumMemberName("coarse")]
    Coarse
}

public enum ColorMixerBand
{
    Red,
    Orange,
    Yellow,
    Green,
    Aqua,
    Blue,
    Purple,
    Magenta
}

public enum LensBaseline
{
    [JsonStringEnumMemberName("standard")]
    Standard,
    [JsonStringEnumMemberName("legacy")]
    Legacy
}

public sealed class LensSettings
{
    [JsonPropertyName("distortion")]
    public bool Distortion { get; set; } = true;

    [JsonPropertyName("chromaticAberration")]
    public bool ChromaticAberration { get; set; } = true;

    [JsonPropertyName("vignetting")]
    public bool Vignetting { get; set; }

    [JsonPropertyName("baseline")]
    [JsonConverter(typeof(StrictCamelCaseEnumConverter<LensBaseline>))]
    public LensBaseline Baseline { get; set; } = LensBaseline.Standard;

    [JsonIgnore]
    public bool HasEdits => Distortion != BaselineDistortion ||
        ChromaticAberration != BaselineChromaticAberration ||
        Vignetting != BaselineVignetting;

    [JsonIgnore]
    public bool BaselineDistortion => Baseline == LensBaseline.Standard;

    [JsonIgnore]
    public bool BaselineChromaticAberration => Baseline == LensBaseline.Standard;

    [JsonIgnore]
    public bool BaselineVignetting => false;

    public void RestoreBaseline()
    {
        Distortion = BaselineDistortion;
        ChromaticAberration = BaselineChromaticAberration;
        Vignetting = BaselineVignetting;
    }

    public LensSettings Clone() => new()
    {
        Distortion = Distortion,
        ChromaticAberration = ChromaticAberration,
        Vignetting = Vignetting,
        Baseline = Baseline
    };

    public static LensSettings Legacy() => new()
    {
        Distortion = false,
        ChromaticAberration = false,
        Vignetting = false,
        Baseline = LensBaseline.Legacy
    };
}

public sealed class GeometrySettings
{
    [JsonPropertyName("vertical")]
    public int Vertical { get; set; }

    [JsonPropertyName("horizontal")]
    public int Horizontal { get; set; }

    [JsonPropertyName("aspect")]
    public int Aspect { get; set; }

    [JsonPropertyName("distortion")]
    public int Distortion { get; set; }

    [JsonIgnore]
    public bool IsIdentity =>
        Vertical == 0 && Horizontal == 0 && Aspect == 0 && Distortion == 0;

    public GeometrySettings Clone() => new()
    {
        Vertical = Vertical,
        Horizontal = Horizontal,
        Aspect = Aspect,
        Distortion = Distortion
    };
}

public sealed class WhiteBalanceSettings
{
    [JsonPropertyName("mode")]
    [JsonConverter(typeof(StrictCamelCaseEnumConverter<WbMode>))]
    public WbMode Mode { get; set; } = WbMode.AsShot;

    [JsonPropertyName("kelvin")]
    public double? Kelvin { get; set; }

    [JsonPropertyName("tint")]
    public double? Tint { get; set; }

    [JsonPropertyName("gains")]
    public double[]? Gains { get; set; }

    [JsonPropertyName("preset")]
    public string? Preset { get; set; }

    public WhiteBalanceSettings Clone() => new()
    {
        Mode = Mode,
        Kelvin = Kelvin,
        Tint = Tint,
        Gains = Gains?.ToArray(),
        Preset = Preset
    };

    [JsonIgnore]
    public bool IsIdentity =>
        Mode == WbMode.AsShot ||
        Mode == WbMode.Picked &&
        Gains is { Length: 3 } &&
        Gains[0] == 1 && Gains[1] == 1 && Gains[2] == 1;
}

public sealed class DetailSettings
{
    public static int GetCaptureSharpenDefault(bool isRawSource) =>
        isRawSource ? 25 : 0;

    [JsonPropertyName("captureSharpen")]
    public int? CaptureSharpen { get; set; }

    [JsonPropertyName("noiseReduction")]
    [JsonConverter(typeof(StrictCamelCaseEnumConverter<FbddMode>))]
    public FbddMode NoiseReduction { get; set; } = FbddMode.Off;

    [JsonPropertyName("chromaNr")]
    public int ChromaNr { get; set; }

    public int ResolveCaptureSharpen(bool isRawSource) =>
        CaptureSharpen ?? GetCaptureSharpenDefault(isRawSource);

    public DetailSettings Clone() => new()
    {
        CaptureSharpen = CaptureSharpen,
        NoiseReduction = NoiseReduction,
        ChromaNr = ChromaNr
    };
}

public sealed class EffectsSettings
{
    [JsonPropertyName("vignette")]
    public int Vignette { get; set; }

    [JsonPropertyName("midpoint")]
    public int Midpoint { get; set; } = 50;

    [JsonPropertyName("grain")]
    public int Grain { get; set; }

    [JsonPropertyName("grainSize")]
    [JsonConverter(typeof(StrictCamelCaseEnumConverter<GrainSize>))]
    public GrainSize GrainSize { get; set; } = GrainSize.Medium;

    [JsonIgnore]
    public bool HasActivePixels => Vignette != 0 || Grain != 0;

    public EffectsSettings Clone() => new()
    {
        Vignette = Vignette,
        Midpoint = Midpoint,
        Grain = Grain,
        GrainSize = GrainSize
    };
}

public sealed class ColorMixerBandSettings
{
    [JsonPropertyName("hue")]
    public int Hue { get; set; }

    [JsonPropertyName("saturation")]
    public int Saturation { get; set; }

    [JsonPropertyName("luminance")]
    public int Luminance { get; set; }

    [JsonIgnore]
    public bool HasActivePixels =>
        Hue != 0 || Saturation != 0 || Luminance != 0;

    public ColorMixerBandSettings Clone() => new()
    {
        Hue = Hue,
        Saturation = Saturation,
        Luminance = Luminance
    };
}

public sealed class ColorMixerSettings
{
    [JsonPropertyName("red")]
    public ColorMixerBandSettings Red { get; set; } = new();

    [JsonPropertyName("orange")]
    public ColorMixerBandSettings Orange { get; set; } = new();

    [JsonPropertyName("yellow")]
    public ColorMixerBandSettings Yellow { get; set; } = new();

    [JsonPropertyName("green")]
    public ColorMixerBandSettings Green { get; set; } = new();

    [JsonPropertyName("aqua")]
    public ColorMixerBandSettings Aqua { get; set; } = new();

    [JsonPropertyName("blue")]
    public ColorMixerBandSettings Blue { get; set; } = new();

    [JsonPropertyName("purple")]
    public ColorMixerBandSettings Purple { get; set; } = new();

    [JsonPropertyName("magenta")]
    public ColorMixerBandSettings Magenta { get; set; } = new();

    [JsonIgnore]
    public bool HasActivePixels =>
        Red?.HasActivePixels == true ||
        Orange?.HasActivePixels == true ||
        Yellow?.HasActivePixels == true ||
        Green?.HasActivePixels == true ||
        Aqua?.HasActivePixels == true ||
        Blue?.HasActivePixels == true ||
        Purple?.HasActivePixels == true ||
        Magenta?.HasActivePixels == true;

    public ColorMixerBandSettings GetBand(ColorMixerBand band) => band switch
    {
        ColorMixerBand.Red => Red,
        ColorMixerBand.Orange => Orange,
        ColorMixerBand.Yellow => Yellow,
        ColorMixerBand.Green => Green,
        ColorMixerBand.Aqua => Aqua,
        ColorMixerBand.Blue => Blue,
        ColorMixerBand.Purple => Purple,
        ColorMixerBand.Magenta => Magenta,
        _ => throw new ArgumentOutOfRangeException(nameof(band))
    };

    public ColorMixerSettings Clone() => new()
    {
        Red = Red?.Clone() ?? new(),
        Orange = Orange?.Clone() ?? new(),
        Yellow = Yellow?.Clone() ?? new(),
        Green = Green?.Clone() ?? new(),
        Aqua = Aqua?.Clone() ?? new(),
        Blue = Blue?.Clone() ?? new(),
        Purple = Purple?.Clone() ?? new(),
        Magenta = Magenta?.Clone() ?? new()
    };
}
