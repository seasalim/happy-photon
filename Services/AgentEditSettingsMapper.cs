using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal static class AgentEditSettingsMapper
{
    public static AgentEditSettingsPatch CreatePatch(AgentEditSettingsInput input)
    {
        if (input.Version != EditSettings.CurrentVersion)
        {
            throw new AgentToolException(
                $"Edit settings version {input.Version} is not supported.");
        }

        return new AgentEditSettingsPatch(
            CreateSettings(input),
            ApplyWb: input.Wb != null,
            ApplyBaseLook: input.BaseLook.HasValue,
            ApplyHighlightReconstruction: input.HlReconstruction != null,
            ApplyLens: input.Lens != null);
    }

    private static EditSettings CreateSettings(AgentEditSettingsInput input)
    {
        var settings = new EditSettings
        {
            Exposure = input.Exposure,
            Wb = CreateWhiteBalance(input.Wb),
            Highlights = input.Highlights,
            Shadows = input.Shadows,
            Brightness = input.Brightness,
            Contrast = input.Contrast,
            Saturation = input.Saturation,
            Vibrance = input.Vibrance,
            BaseLook = input.BaseLook,
            HlReconstruction = ParseHighlightMode(input.HlReconstruction),
            Lens = input.Lens == null
                ? new LensSettings()
                : new LensSettings
                {
                    Distortion = input.Lens.Distortion,
                    ChromaticAberration = input.Lens.ChromaticAberration,
                    Vignetting = input.Lens.Vignetting
                }
        };

        try
        {
            return EditSettingsJson.Deserialize(
                EditSettingsJson.Serialize(settings),
                out _);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new AgentToolException(exception.Message);
        }
    }

    private static WhiteBalanceSettings CreateWhiteBalance(AgentWhiteBalanceInput? input)
    {
        if (input == null)
        {
            return new WhiteBalanceSettings();
        }

        return new WhiteBalanceSettings
        {
            Mode = (input.Mode ?? "").ToLowerInvariant() switch
            {
                "asshot" => WbMode.AsShot,
                "custom" => WbMode.Custom,
                "preset" => WbMode.Preset,
                "picked" => WbMode.Picked,
                _ => throw new AgentToolException(
                    $"White-balance mode '{input.Mode}' is not supported.")
            },
            Kelvin = input.Kelvin,
            Tint = input.Tint,
            Gains = input.Gains?.ToArray(),
            Preset = input.Preset
        };
    }

    private static HlReconstructionMode ParseHighlightMode(string? value) =>
        value?.ToLowerInvariant() switch
        {
            null or "clip" => HlReconstructionMode.Clip,
            "blend" => HlReconstructionMode.Blend,
            _ => throw new AgentToolException(
                $"Highlight reconstruction mode '{value}' is not supported.")
        };
}

internal sealed record AgentEditSettingsPatch(
    EditSettings Settings,
    bool ApplyWb,
    bool ApplyBaseLook,
    bool ApplyHighlightReconstruction,
    bool ApplyLens = false)
{
    public void ApplyTo(EditSettings target)
    {
        target.Exposure = Settings.Exposure;
        target.Highlights = Settings.Highlights;
        target.Shadows = Settings.Shadows;
        target.Brightness = Settings.Brightness;
        target.Contrast = Settings.Contrast;
        target.Saturation = Settings.Saturation;
        target.Vibrance = Settings.Vibrance;
        if (ApplyWb) target.Wb = Settings.Wb.Clone();
        if (ApplyBaseLook) target.BaseLook = Settings.BaseLook;
        if (ApplyHighlightReconstruction)
        {
            target.HlReconstruction = Settings.HlReconstruction;
        }
        if (ApplyLens)
        {
            target.Lens.Distortion = Settings.Lens.Distortion;
            target.Lens.ChromaticAberration = Settings.Lens.ChromaticAberration;
            target.Lens.Vignetting = Settings.Lens.Vignetting;
        }
        target.Curve = Settings.Curve.Clone();
        target.CurveRed = null;
        target.CurveGreen = null;
        target.CurveBlue = null;
        target.Effects = null;
        target.Mixer = null;
        target.AppliedPresetId = null;
    }
}
