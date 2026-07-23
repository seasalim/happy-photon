using System.Text.Json.Serialization;

namespace HappyPhoton.Models;

/// <summary>
/// Non-destructive edit parameters stored in sidecar files.
/// </summary>
public class EditSettings
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>Exposure compensation in EV (-3.0 to +3.0)</summary>
    [JsonPropertyName("exposure")]
    public double Exposure { get; set; } = 0.0;

    /// <summary>Color temperature adjustment (-100 cool to +100 warm)</summary>
    [JsonPropertyName("temperature")]
    public int Temperature { get; set; } = 0;

    /// <summary>Brightness adjustment (-100 to +100)</summary>
    [JsonPropertyName("brightness")]
    public int Brightness { get; set; } = 0;

    /// <summary>Contrast adjustment (-100 to +100)</summary>
    [JsonPropertyName("contrast")]
    public int Contrast { get; set; } = 0;

    /// <summary>Saturation adjustment (-100 to +100)</summary>
    [JsonPropertyName("saturation")]
    public int Saturation { get; set; } = 0;

    /// <summary>Vibrance adjustment (-100 to +100)</summary>
    [JsonPropertyName("vibrance")]
    public int Vibrance { get; set; } = 0;

    /// <summary>Shadows adjustment (-100 to +100)</summary>
    [JsonPropertyName("shadows")]
    public int Shadows { get; set; } = 0;

    /// <summary>Highlights adjustment (-100 to +100)</summary>
    [JsonPropertyName("highlights")]
    public int Highlights { get; set; } = 0;

    /// <summary>Rotation in degrees clockwise (0, 90, 180, 270)</summary>
    [JsonPropertyName("rotation")]
    public int Rotation { get; set; } = 0;

    /// <summary>Fine horizon rotation in degrees clockwise.</summary>
    [JsonPropertyName("horizon_rotation")]
    public double HorizonRotation { get; set; } = 0.0;

    /// <summary>Crop region using normalized coordinates (0.0 to 1.0)</summary>
    [JsonPropertyName("crop")]
    public CropRegion? Crop { get; set; }

    /// <summary>Tone curve for fine tonal adjustments</summary>
    [JsonPropertyName("curve")]
    public CurveData Curve { get; set; } = new();

    /// <summary>ID of the preset that was applied, or null if no preset is active</summary>
    [JsonPropertyName("applied_preset_id")]
    public string? AppliedPresetId { get; set; }

    public bool HasEdits => Exposure != 0.0 || Temperature != 0 || Brightness != 0 || Contrast != 0 ||
                          Saturation != 0 || Vibrance != 0 || Shadows != 0 || Highlights != 0 ||
                          Rotation != 0 || HorizonRotation != 0.0 || (Crop != null && !Crop.IsFullImage) ||
                          !Curve.IsIdentity() || AppliedPresetId != null;

    public EditSettings Clone() => new()
    {
        Version = Version,
        Exposure = Exposure,
        Temperature = Temperature,
        Brightness = Brightness,
        Contrast = Contrast,
        Saturation = Saturation,
        Vibrance = Vibrance,
        Shadows = Shadows,
        Highlights = Highlights,
        Rotation = Rotation,
        HorizonRotation = HorizonRotation,
        Crop = Crop?.Clone(),
        Curve = Curve.Clone(),
        AppliedPresetId = AppliedPresetId
    };

    public void Reset()
    {
        Exposure = 0.0;
        Temperature = 0;
        Brightness = 0;
        Contrast = 0;
        Saturation = 0;
        Vibrance = 0;
        Shadows = 0;
        Highlights = 0;
        Rotation = 0;
        HorizonRotation = 0.0;
        Curve.Reset();
        AppliedPresetId = null;
    }

    public void CopyFrom(EditSettings source)
    {
        Exposure = source.Exposure;
        Temperature = source.Temperature;
        Brightness = source.Brightness;
        Contrast = source.Contrast;
        Saturation = source.Saturation;
        Vibrance = source.Vibrance;
        Shadows = source.Shadows;
        Highlights = source.Highlights;
        Rotation = source.Rotation;
        HorizonRotation = source.HorizonRotation;
        Crop = source.Crop?.Clone();
        Curve = source.Curve.Clone();
        AppliedPresetId = source.AppliedPresetId;
    }

    public bool EqualsIgnoringRotation(EditSettings other)
    {
        return Exposure == other.Exposure && Temperature == other.Temperature &&
               Brightness == other.Brightness && Contrast == other.Contrast &&
               Saturation == other.Saturation && Vibrance == other.Vibrance &&
               Shadows == other.Shadows && Highlights == other.Highlights &&
               AppliedPresetId == other.AppliedPresetId;
    }
}
