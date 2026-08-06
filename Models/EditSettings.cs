using System.Text.Json.Serialization;

namespace HappyPhoton.Models;

/// <summary>
/// Versioned non-destructive edit parameters stored in the catalog and preset files.
/// </summary>
public class EditSettings
{
    public const int CurrentVersion = 2;

    [JsonPropertyName("version")]
    [JsonPropertyOrder(0)]
    public int Version { get; set; } = CurrentVersion;

    /// <summary>Exposure compensation in EV (-3.0 to +3.0)</summary>
    [JsonPropertyName("exposure")]
    [JsonPropertyOrder(1)]
    public double Exposure { get; set; } = 0.0;

    [JsonPropertyName("wb")]
    [JsonPropertyOrder(2)]
    public WhiteBalanceSettings Wb { get; set; } = new();

    /// <summary>Brightness adjustment (-100 to +100)</summary>
    [JsonPropertyName("brightness")]
    [JsonPropertyOrder(5)]
    public int Brightness { get; set; } = 0;

    /// <summary>Contrast adjustment (-100 to +100)</summary>
    [JsonPropertyName("contrast")]
    [JsonPropertyOrder(6)]
    public int Contrast { get; set; } = 0;

    /// <summary>Saturation adjustment (-100 to +100)</summary>
    [JsonPropertyName("saturation")]
    [JsonPropertyOrder(7)]
    public int Saturation { get; set; } = 0;

    /// <summary>Vibrance adjustment (-100 to +100)</summary>
    [JsonPropertyName("vibrance")]
    [JsonPropertyOrder(8)]
    public int Vibrance { get; set; } = 0;

    /// <summary>Highlights adjustment (-100 to +100)</summary>
    [JsonPropertyName("highlights")]
    [JsonPropertyOrder(3)]
    public int Highlights { get; set; } = 0;

    /// <summary>Shadows adjustment (-100 to +100)</summary>
    [JsonPropertyName("shadows")]
    [JsonPropertyOrder(4)]
    public int Shadows { get; set; } = 0;

    [JsonPropertyName("baseLook")]
    [JsonPropertyOrder(9)]
    public bool? BaseLook { get; set; }

    [JsonPropertyName("hlReconstruction")]
    [JsonPropertyOrder(10)]
    [JsonConverter(typeof(StrictCamelCaseEnumConverter<HlReconstructionMode>))]
    public HlReconstructionMode HlReconstruction { get; set; } = HlReconstructionMode.Clip;

    [JsonPropertyName("detail")]
    [JsonPropertyOrder(11)]
    public DetailSettings Detail { get; set; } = new();

    /// <summary>Rotation in degrees clockwise (0, 90, 180, 270)</summary>
    [JsonPropertyName("rotation")]
    [JsonPropertyOrder(12)]
    public int Rotation { get; set; } = 0;

    /// <summary>Fine horizon rotation in degrees clockwise.</summary>
    [JsonPropertyName("horizon_rotation")]
    [JsonPropertyOrder(13)]
    public double HorizonRotation { get; set; } = 0.0;

    /// <summary>Crop region using normalized coordinates (0.0 to 1.0)</summary>
    [JsonPropertyName("crop")]
    [JsonPropertyOrder(14)]
    public CropRegion? Crop { get; set; }

    /// <summary>Tone curve for fine tonal adjustments</summary>
    [JsonPropertyName("curve")]
    [JsonPropertyOrder(15)]
    public CurveData Curve { get; set; } = new();

    /// <summary>ID of the preset that was applied, or null if no preset is active</summary>
    [JsonPropertyName("applied_preset_id")]
    [JsonPropertyOrder(16)]
    public string? AppliedPresetId { get; set; }

    [JsonIgnore]
    public bool HasEdits => Exposure != 0.0 || !Wb.IsIdentity ||
                          Brightness != 0 || Contrast != 0 ||
                          Saturation != 0 || Vibrance != 0 || Shadows != 0 || Highlights != 0 ||
                          BaseLook != null || HlReconstruction != HlReconstructionMode.Clip ||
                          Detail.CaptureSharpen != null || Detail.NoiseReduction != FbddMode.Off ||
                          Detail.ChromaNr != 0 ||
                          Rotation != 0 || HorizonRotation != 0.0 || (Crop != null && !Crop.IsFullImage) ||
                          !Curve.IsIdentity() || AppliedPresetId != null;

    public EditSettings Clone() => new()
    {
        Version = Version,
        Exposure = Exposure,
        Wb = Wb?.Clone() ?? new WhiteBalanceSettings(),
        Brightness = Brightness,
        Contrast = Contrast,
        Saturation = Saturation,
        Vibrance = Vibrance,
        Shadows = Shadows,
        Highlights = Highlights,
        BaseLook = BaseLook,
        HlReconstruction = HlReconstruction,
        Detail = Detail?.Clone() ?? new DetailSettings(),
        Rotation = Rotation,
        HorizonRotation = HorizonRotation,
        Crop = Crop?.Clone(),
        Curve = Curve?.Clone() ?? new CurveData(),
        AppliedPresetId = AppliedPresetId
    };

    public bool EqualsIgnoringRotation(EditSettings other)
    {
        return Exposure == other.Exposure && Wb.Mode == other.Wb.Mode &&
               Enumerable.SequenceEqual(Wb.Gains ?? [], other.Wb.Gains ?? []) &&
               Wb.Kelvin == other.Wb.Kelvin && Wb.Tint == other.Wb.Tint &&
               Wb.Preset == other.Wb.Preset &&
               Brightness == other.Brightness && Contrast == other.Contrast &&
               Saturation == other.Saturation && Vibrance == other.Vibrance &&
               Shadows == other.Shadows && Highlights == other.Highlights &&
               BaseLook == other.BaseLook &&
               HlReconstruction == other.HlReconstruction &&
               Detail.CaptureSharpen == other.Detail.CaptureSharpen &&
               Detail.NoiseReduction == other.Detail.NoiseReduction &&
               Detail.ChromaNr == other.Detail.ChromaNr &&
               AppliedPresetId == other.AppliedPresetId;
    }
}
