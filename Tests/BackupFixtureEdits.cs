using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

internal static class BackupFixtureEdits
{
    // Complete snapshots of a plausible editing session, not repeated filler JSON.
    // Original -> tonal edits -> crop -> curve/HSL -> one or two locals.
    // Both workloads use the same recipe; only tonal-step count differs.
    internal static (string Label, string Json)[] CreateHistory(Random random, int count)
    {
        var settings = new EditSettings();
        var snapshots = new (string Label, string Json)[count];
        snapshots[0] = ("Original", EditSettingsJson.Serialize(settings));
        for (var seq = 1; seq < count; seq++)
        {
            var label = "Tone";
            settings.Exposure = Number(random, -2, 2);
            settings.Contrast = random.Next(-40, 41);
            settings.Highlights = random.Next(-60, 41);
            settings.Shadows = random.Next(-30, 61);
            settings.Saturation = random.Next(-25, 26);
            settings.Vibrance = random.Next(-20, 41);
            if (seq == count - 3)
            {
                label = "Crop";
                settings.Crop = new CropRegion
                {
                    Left = Number(random, .01, .15), Top = Number(random, .01, .15),
                    Right = Number(random, .85, .99), Bottom = Number(random, .85, .99)
                };
            }
            if (seq == count - 2)
            {
                label = "Curve and HSL";
                settings.Curve.AddPointAndReturnIndex(.25, Number(random, .15, .35));
                settings.Curve.AddPointAndReturnIndex(.75, Number(random, .65, .85));
                settings.Mixer = new ColorMixerSettings();
                foreach (var band in Enum.GetValues<ColorMixerBand>())
                {
                    var values = settings.Mixer.GetBand(band);
                    values.Hue = random.Next(-30, 31);
                    values.Saturation = random.Next(-40, 41);
                    values.Luminance = random.Next(-25, 26);
                }
            }
            if (seq == count - 1)
            {
                label = "Local adjustments";
                settings.Locals = Enumerable.Range(1, random.Next(1, 3)).Select(ordinal =>
                {
                    var guid = new byte[16];
                    random.NextBytes(guid);
                    return new LocalAdjustment
                    {
                        Id = new Guid(guid).ToString("N"), Ordinal = ordinal,
                        Type = ordinal == 1 ? "linear" : "radial",
                        Cu = Number(random, .1, .9), Cv = Number(random, .1, .9),
                        Angle = random.Next(360), Feather = Number(random, .1, .7),
                        Exposure = Number(random, -1, 1),
                        Rx = Number(random, .1, .4), Ry = Number(random, .1, .4)
                    };
                }).ToList();
            }
            // The production serializer clones and clamps before emitting v4 JSON.
            snapshots[seq] = (label, EditSettingsJson.Serialize(settings));
        }
        return snapshots;
    }

    private static double Number(Random random, double min, double max) =>
        Math.Round(min + random.NextDouble() * (max - min), 2);
}
