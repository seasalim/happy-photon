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
