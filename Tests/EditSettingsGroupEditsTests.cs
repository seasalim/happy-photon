using HappyPhoton.Models;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class EditSettingsGroupEditsTests
{
    [Fact]
    public void DefaultsAndNonGroupMarkersHaveNoOwnEdits()
    {
        Assert.Equal(14, EditSettingsTransfer.Groups.Count);

        foreach (var settings in new[]
        {
            new EditSettings(),
            new EditSettings { BaseLook = true },
            new EditSettings { BaseLook = false },
            new EditSettings { Rotation = 90, AppliedPresetId = "marker" },
            new EditSettings { Effects = new EffectsSettings { Midpoint = 75, GrainSize = GrainSize.Coarse } },
            new EditSettings { Mixer = new(), Geometry = new(), CurveRed = new(), CurveGreen = new(), CurveBlue = new() }
        })
        {
            Assert.All(EditSettingsTransfer.Groups, group =>
                Assert.False(group.DiffersFromDefault(settings), group.Name));
        }
    }

    [Fact]
    public void EverySavedSettingBelongsToExactlyOneGroup()
    {
        foreach (var property in new[] { "Exposure", "Brightness", "Contrast",
            "Shadows", "Highlights", "Whites", "Blacks", "HlReconstruction" })
        {
            Check("Adjustments", settings => Set(settings, property));
        }

        Check("Presence", settings => settings.Texture = 1);
        Check("Presence", settings => settings.Clarity = 1);
        Check("Presence", settings => settings.Vibrance = 1);
        Check("Presence", settings => settings.Saturation = 1);
        Check("White Balance", settings => settings.Wb = new() { Mode = WbMode.Custom, Kelvin = 6000 });
        Check("White Balance", settings => settings.Wb = new() { Mode = WbMode.Custom, Tint = 10 });
        Check("White Balance", settings => settings.Wb = new() { Mode = WbMode.Preset, Preset = "daylight" });
        Check("White Balance", settings => settings.Wb = new() { Mode = WbMode.Picked, Gains = [2, 1, 1] });

        foreach (var channel in new[] { "Curve", "CurveRed", "CurveGreen", "CurveBlue" })
        {
            Check("Tone Curve", settings =>
            {
                var curve = new CurveData();
                curve.AddPointAndReturnIndex(.5, .6);
                typeof(EditSettings).GetProperty(channel)!.SetValue(settings, curve);
            });
        }

        foreach (var band in Enum.GetValues<ColorMixerBand>())
        {
            foreach (var property in new[] { "Hue", "Saturation", "Luminance" })
            {
                Check("Color Mixer", settings =>
                {
                    settings.Mixer = new();
                    Set(settings.Mixer.GetBand(band), property);
                });
            }
        }

        foreach (var property in new[] { "CaptureSharpen", "LuminanceNr", "ChromaNr" })
        {
            Check("Detail", settings => Set(settings.Detail, property));
        }

        Check("Detail", settings => settings.Detail.CaptureSharpen = 0);
        Check("Effects", settings => settings.Effects = new() { Vignette = -1 });
        Check("Effects", settings => settings.Effects = new() { Vignette = -1, Midpoint = 75 });
        Check("Effects", settings => settings.Effects = new() { Grain = 1 });
        Check("Effects", settings => settings.Effects = new() { Grain = 1, GrainSize = GrainSize.Coarse });

        foreach (var property in new[] { "Distortion", "ChromaticAberration", "Vignetting" })
        {
            Check("Optics", settings =>
            {
                var member = typeof(LensSettings).GetProperty(property)!;
                member.SetValue(settings.Lens, !(bool)member.GetValue(settings.Lens)!);
            });
        }

        foreach (var property in new[] { "Vertical", "Horizontal", "Aspect", "Distortion" })
        {
            Check("Geometry", settings =>
            {
                settings.Geometry = new();
                Set(settings.Geometry, property);
            });
        }

        Check("Camera Profile", settings => settings.RawProfile = new());
        Check("Lens Profile", settings => settings.Lens.ProfileOverride = "lens");
        Check("Crop & Straighten", settings => settings.Crop = new() { Left = .1 });
        Check("Crop & Straighten", settings => settings.HorizonRotation = 1);
        Check("Locals", settings => settings.Locals = [new()]);
        Check("Spot Removal", settings => settings.Repairs = [new()]);
    }

    private static void Check(string expected, Action<EditSettings> edit)
    {
        var settings = new EditSettings();
        edit(settings);
        var edited = EditSettingsTransfer.Groups.Where(group => group.DiffersFromDefault(settings));

        Assert.Equal(expected, Assert.Single(edited).Name);
    }

    private static void Set(object target, string name)
    {
        var property = target.GetType().GetProperty(name)!;
        object value = name == "HlReconstruction" ? HlReconstructionMode.Blend :
            property.PropertyType == typeof(double) ? (object)1d : 1;
        property.SetValue(target, value);
    }
}
