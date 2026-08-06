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
        settings.Curve ??= new CurveData();
        if (settings.Curve.Points == null)
        {
            throw new JsonException("Tone curve points cannot be null.");
        }
        settings.Curve.BuildLookupTable();
        return changed;
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
