using System.Text.Json.Serialization;

namespace HappyPhoton.Models;

/// <summary>
/// Versioned non-destructive edit parameters stored in the catalog and preset files.
/// </summary>
public class EditSettings
{
    public const int CurrentVersion = 3;

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

    [JsonPropertyName("effects")]
    [JsonPropertyOrder(12)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EffectsSettings? Effects { get; set; }

    [JsonPropertyName("lens")]
    [JsonPropertyOrder(13)]
    public LensSettings Lens { get; set; } = new();

    /// <summary>Rotation in degrees clockwise (0, 90, 180, 270)</summary>
    [JsonPropertyName("rotation")]
    [JsonPropertyOrder(14)]
    public int Rotation { get; set; } = 0;

    /// <summary>Fine horizon rotation in degrees clockwise.</summary>
    [JsonPropertyName("horizon_rotation")]
    [JsonPropertyOrder(15)]
    public double HorizonRotation { get; set; } = 0.0;

    /// <summary>Crop region using normalized coordinates (0.0 to 1.0)</summary>
    [JsonPropertyName("crop")]
    [JsonPropertyOrder(16)]
    public CropRegion? Crop { get; set; }

    /// <summary>Tone curve for fine tonal adjustments</summary>
    [JsonPropertyName("curve")]
    [JsonPropertyOrder(17)]
    public CurveData Curve { get; set; } = new();

    [JsonPropertyName("curveRed")]
    [JsonPropertyOrder(18)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CurveData? CurveRed { get; set; }

    [JsonPropertyName("curveGreen")]
    [JsonPropertyOrder(19)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CurveData? CurveGreen { get; set; }

    [JsonPropertyName("curveBlue")]
    [JsonPropertyOrder(20)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CurveData? CurveBlue { get; set; }

    /// <summary>ID of the preset that was applied, or null if no preset is active</summary>
    [JsonPropertyName("applied_preset_id")]
    [JsonPropertyOrder(21)]
    public string? AppliedPresetId { get; set; }

    [JsonPropertyName("rawProfile")]
    [JsonPropertyOrder(22)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RawProfileSelection? RawProfile { get; set; }

    [JsonPropertyName("mixer")]
    [JsonPropertyOrder(23)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ColorMixerSettings? Mixer { get; set; }

    [JsonPropertyName("geometry")]
    [JsonPropertyOrder(24)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeometrySettings? Geometry { get; set; }

    [JsonIgnore]
    public bool HasEdits => Exposure != 0.0 || !Wb.IsIdentity ||
                          Brightness != 0 || Contrast != 0 ||
                          Saturation != 0 || Vibrance != 0 || Shadows != 0 || Highlights != 0 ||
                          BaseLook != null || HlReconstruction != HlReconstructionMode.Clip ||
                          Detail.CaptureSharpen != null || Detail.LuminanceNr != 0 ||
                          Detail.ChromaNr != 0 ||
                          Effects?.HasActivePixels == true ||
                          Mixer?.HasActivePixels == true ||
                          Geometry?.IsIdentity == false ||
                          Lens.HasEdits ||
                          Rotation != 0 || HorizonRotation != 0.0 || (Crop != null && !Crop.IsFullImage) ||
                          !Curve.IsIdentity() ||
                          (CurveRed is { } red && !red.IsIdentity()) ||
                          (CurveGreen is { } green && !green.IsIdentity()) ||
                          (CurveBlue is { } blue && !blue.IsIdentity()) ||
                          AppliedPresetId != null ||
                          RawProfile != null;

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
        Effects = Effects?.Clone(),
        Lens = Lens?.Clone() ?? new LensSettings(),
        Rotation = Rotation,
        HorizonRotation = HorizonRotation,
        Crop = Crop?.Clone(),
        Curve = Curve?.Clone() ?? new CurveData(),
        CurveRed = CurveRed?.Clone(),
        CurveGreen = CurveGreen?.Clone(),
        CurveBlue = CurveBlue?.Clone(),
        AppliedPresetId = AppliedPresetId,
        RawProfile = RawProfile?.Clone(),
        Mixer = Mixer?.Clone(),
        Geometry = Geometry?.Clone()
    };

    public bool HasSameEdits(EditSettings other)
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
               Detail.LuminanceNr == other.Detail.LuminanceNr &&
               Detail.ChromaNr == other.Detail.ChromaNr &&
               EffectsMatch(Effects, other.Effects) &&
               MixersMatch(Mixer, other.Mixer) &&
               GeometryMatches(Geometry, other.Geometry) &&
               Lens.Distortion == other.Lens.Distortion &&
               Lens.ChromaticAberration == other.Lens.ChromaticAberration &&
               Lens.Vignetting == other.Lens.Vignetting &&
               Rotation == other.Rotation &&
               HorizonRotation == other.HorizonRotation &&
               CropsMatch(Crop, other.Crop) &&
               CurvesMatch(Curve, other.Curve) &&
               CurvesMatch(CurveRed, other.CurveRed) &&
               CurvesMatch(CurveGreen, other.CurveGreen) &&
               CurvesMatch(CurveBlue, other.CurveBlue) &&
               ProfilesEqual(RawProfile, other.RawProfile);
    }

    private static bool CropsMatch(CropRegion? left, CropRegion? right) =>
        ReferenceEquals(left, right) ||
        left != null && right != null &&
        left.Left == right.Left && left.Top == right.Top &&
        left.Right == right.Right && left.Bottom == right.Bottom;

    private static bool CurvesMatch(CurveData? left, CurveData? right)
    {
        if (left == null || right == null)
        {
            return left == right;
        }

        return left.Points.SequenceEqual(right.Points);
    }

    private static bool ProfilesEqual(
        RawProfileSelection? left,
        RawProfileSelection? right) =>
        ReferenceEquals(left, right) ||
        left != null && right != null &&
        left.Source == right.Source &&
        string.Equals(left.Location, right.Location, StringComparison.Ordinal) &&
        string.Equals(
            left.ContentHash,
            right.ContentHash,
            StringComparison.OrdinalIgnoreCase);

    private static bool EffectsMatch(
        EffectsSettings? left,
        EffectsSettings? right)
    {
        var leftActive = left?.HasActivePixels == true;
        var rightActive = right?.HasActivePixels == true;
        if (!leftActive || !rightActive)
        {
            return leftActive == rightActive;
        }

        return left!.Vignette == right!.Vignette &&
               left.Midpoint == right.Midpoint &&
               left.Grain == right.Grain &&
               left.GrainSize == right.GrainSize;
    }

    private static bool MixersMatch(
        ColorMixerSettings? left,
        ColorMixerSettings? right)
    {
        var leftActive = left?.HasActivePixels == true;
        var rightActive = right?.HasActivePixels == true;
        if (!leftActive || !rightActive)
        {
            return leftActive == rightActive;
        }

        foreach (var band in Enum.GetValues<ColorMixerBand>())
        {
            var leftBand = left!.GetBand(band);
            var rightBand = right!.GetBand(band);
            if (leftBand.Hue != rightBand.Hue ||
                leftBand.Saturation != rightBand.Saturation ||
                leftBand.Luminance != rightBand.Luminance)
            {
                return false;
            }
        }

        return true;
    }

    private static bool GeometryMatches(
        GeometrySettings? left,
        GeometrySettings? right)
    {
        var leftActive = left?.IsIdentity == false;
        var rightActive = right?.IsIdentity == false;
        if (!leftActive || !rightActive)
        {
            return leftActive == rightActive;
        }

        return left!.Vertical == right!.Vertical &&
               left.Horizontal == right.Horizontal &&
               left.Aspect == right.Aspect &&
               left.Distortion == right.Distortion;
    }
}

public enum ToneCurveChannel
{
    Composite,
    Red,
    Green,
    Blue
}
