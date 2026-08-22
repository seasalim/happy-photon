using System.Text.Json;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal static class EditSettingsJson
{
    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        WriteIndented = false
    };

    public static string Serialize(EditSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var current = settings.Clone();
        if (current.Version != EditSettings.CurrentVersion)
        {
            throw new NotSupportedException(
                $"Edit settings version {current.Version} is not supported.");
        }
        Clamp(current);
        return JsonSerializer.Serialize(current, CompactOptions);
    }

    public static EditSettings Deserialize(string json, out bool wasClamped)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("version", out var versionElement) ||
            !versionElement.TryGetInt32(out var documentVersion) ||
            documentVersion != EditSettings.CurrentVersion)
        {
            throw new JsonException("Edit settings document must declare version 2.");
        }

        var settings = JsonSerializer.Deserialize<EditSettings>(json, CompactOptions)
            ?? throw new JsonException("Edit settings document is null.");
        if (settings.Version != EditSettings.CurrentVersion)
        {
            throw new JsonException(
                $"Edit settings document version {settings.Version} does not match v2.");
        }

        wasClamped = Clamp(settings);
        return settings;
    }

    private static bool Clamp(EditSettings settings)
    {
        var changed = false;
        settings.Exposure = Clamp(settings.Exposure, -3, 3, ref changed);
        settings.Highlights = Clamp(settings.Highlights, -100, 100, ref changed);
        settings.Shadows = Clamp(settings.Shadows, -100, 100, ref changed);
        settings.Brightness = Clamp(settings.Brightness, -100, 100, ref changed);
        settings.Contrast = Clamp(settings.Contrast, -100, 100, ref changed);
        settings.Saturation = Clamp(settings.Saturation, -100, 100, ref changed);
        settings.Vibrance = Clamp(settings.Vibrance, -100, 100, ref changed);
        settings.HorizonRotation = Clamp(settings.HorizonRotation, -5, 5, ref changed);

        settings.Wb ??= new WhiteBalanceSettings();
        settings.Wb.Kelvin = ClampNullable(settings.Wb.Kelvin, 2000, 12000, ref changed);
        settings.Wb.Tint = ClampNullable(settings.Wb.Tint, -100, 100, ref changed);
        if (settings.Wb.Gains != null)
        {
            if (settings.Wb.Gains.Length != 3)
            {
                throw new JsonException("White-balance gains must contain three values.");
            }
            for (var index = 0; index < settings.Wb.Gains.Length; index++)
            {
                settings.Wb.Gains[index] =
                    Clamp(settings.Wb.Gains[index], 0.2, 5, ref changed);
            }
        }
        ValidateWhiteBalance(settings.Wb);

        settings.Detail ??= new DetailSettings();
        settings.Detail.CaptureSharpen = ClampNullable(
            settings.Detail.CaptureSharpen, 0, 100, ref changed);
        settings.Detail.ChromaNr = Clamp(settings.Detail.ChromaNr, 0, 100, ref changed);
        ValidateRawProfile(settings.RawProfile);
        if (settings.Effects != null)
        {
            if (!Enum.IsDefined(settings.Effects.GrainSize))
            {
                throw new JsonException("Effects grain size is not supported.");
            }
            settings.Effects.Vignette = Clamp(
                settings.Effects.Vignette, -100, 100, ref changed);
            settings.Effects.Midpoint = Clamp(
                settings.Effects.Midpoint, 0, 100, ref changed);
            settings.Effects.Grain = Clamp(
                settings.Effects.Grain, 0, 100, ref changed);
            if (!settings.Effects.HasActivePixels)
            {
                settings.Effects = null;
            }
        }
        if (settings.Mixer != null)
        {
            ClampMixer(settings.Mixer, ref changed);
            if (!settings.Mixer.HasActivePixels)
            {
                settings.Mixer = null;
            }
        }
        settings.Curve ??= new CurveData();
        RebuildCurve(settings.Curve);
        RebuildCurve(settings.CurveRed);
        RebuildCurve(settings.CurveGreen);
        RebuildCurve(settings.CurveBlue);
        return changed;
    }

    private static void ClampMixer(ColorMixerSettings mixer, ref bool changed)
    {
        mixer.Red ??= new ColorMixerBandSettings();
        mixer.Orange ??= new ColorMixerBandSettings();
        mixer.Yellow ??= new ColorMixerBandSettings();
        mixer.Green ??= new ColorMixerBandSettings();
        mixer.Aqua ??= new ColorMixerBandSettings();
        mixer.Blue ??= new ColorMixerBandSettings();
        mixer.Purple ??= new ColorMixerBandSettings();
        mixer.Magenta ??= new ColorMixerBandSettings();
        foreach (var band in Enum.GetValues<ColorMixerBand>())
        {
            var values = mixer.GetBand(band);
            values.Hue = Clamp(values.Hue, -100, 100, ref changed);
            values.Saturation = Clamp(
                values.Saturation, -100, 100, ref changed);
            values.Luminance = Clamp(
                values.Luminance, -100, 100, ref changed);
        }
    }

    private static void RebuildCurve(CurveData? curve)
    {
        if (curve == null)
        {
            return;
        }
        if (curve.Points == null)
        {
            throw new JsonException("Tone curve points cannot be null.");
        }
        curve.BuildLookupTable();
    }

    private static void ValidateRawProfile(RawProfileSelection? profile)
    {
        if (profile == null)
        {
            return;
        }
        if (!Enum.IsDefined(profile.Source))
        {
            throw new JsonException("RAW profile source is not supported.");
        }
        if (profile.Source != RawProfileSource.Embedded &&
            string.IsNullOrWhiteSpace(profile.Location))
        {
            throw new JsonException("External RAW profiles require a location.");
        }
        if (profile.ContentHash.Length != 64 ||
            profile.ContentHash.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new JsonException(
                "RAW profile contentHash must be a SHA-256 hexadecimal value.");
        }
        profile.ContentHash = profile.ContentHash.ToLowerInvariant();
    }

    private static void ValidateWhiteBalance(WhiteBalanceSettings whiteBalance)
    {
        if (whiteBalance.Mode == WbMode.Picked &&
            whiteBalance.Gains == null)
        {
            throw new JsonException(
                $"{whiteBalance.Mode} white balance requires gains.");
        }
        if (whiteBalance.Mode is WbMode.Custom or WbMode.Preset &&
            (!whiteBalance.Kelvin.HasValue || !whiteBalance.Tint.HasValue))
        {
            throw new JsonException(
                $"{whiteBalance.Mode} white balance requires kelvin and tint.");
        }
        if (whiteBalance.Mode == WbMode.Preset &&
            string.IsNullOrWhiteSpace(whiteBalance.Preset))
        {
            throw new JsonException("Preset white balance requires a preset name.");
        }
    }

    private static int Clamp(int value, int minimum, int maximum, ref bool changed)
    {
        var clamped = Math.Clamp(value, minimum, maximum);
        changed |= clamped != value;
        return clamped;
    }

    private static double Clamp(
        double value,
        double minimum,
        double maximum,
        ref bool changed)
    {
        if (!double.IsFinite(value))
        {
            throw new JsonException("Edit settings contain a non-finite number.");
        }
        var clamped = Math.Clamp(value, minimum, maximum);
        changed |= clamped != value;
        return clamped;
    }

    private static int? ClampNullable(
        int? value,
        int minimum,
        int maximum,
        ref bool changed) =>
        value.HasValue ? Clamp(value.Value, minimum, maximum, ref changed) : null;

    private static double? ClampNullable(
        double? value,
        double minimum,
        double maximum,
        ref bool changed) =>
        value.HasValue ? Clamp(value.Value, minimum, maximum, ref changed) : null;
}
