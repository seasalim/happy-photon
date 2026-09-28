using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

internal sealed record SyncParityCase(string Name, EditSettings Settings);

internal static class SyncTransferParityCorpus
{
    internal const string PresetId = "user_sync_parity";

    internal const string DeletedPresetId = "user_sync_deleted";

    internal const int BackupSeed = 292031;

    internal static IReadOnlyList<SyncParityCase> Sources()
    {
        var cases = GoldenTestCases.Assets.SelectMany(asset => asset.SettingsCases)
            .DistinctBy(item => item.Slug)
            .Select(item => new SyncParityCase($"golden/{item.Slug}", item.CreateSettings()))
            .ToList();
        // This is the pinned, deterministic-ID form of LocalsFusedBaselineTests' LH8.
        cases.Add(new("LH8", LocalsBrushWorkloads.Settings(true)));
        cases.Add(new("BCap", LocalsBrushProduction.Attach(
            LocalsBrushWorkloads.Settings(true), LocalsBrushWorkloads.Create(true, 1600, 1067))));
        cases.Add(new("LK", CreateLook()));
        var random = new Random(BackupSeed);

        // Ten five-document sessions retain all generator stages, including locals.
        for (var session = 0; session < 10; session++)
        {
            var history = BackupFixtureEdits.CreateHistory(random, 5);

            for (var step = 0; step < history.Length; step++)
            {
                cases.Add(new($"backup/{session:D2}/{step}-{history[step].Label}",
                    EditSettingsJson.Deserialize(history[step].Json, out _)));
            }
        }

        return cases;
    }

    internal static IReadOnlyList<SyncParityCase> Destinations() =>
    [
        new("locals", LocalsBrushWorkloads.Settings(true)),
        new("crop", new EditSettings
        {
            Rotation = 90,
            HorizonRotation = 3.25,
            Crop = new CropRegion { Left = .13, Top = .17, Right = .89, Bottom = .93 }
        }),
        new("geometry", new EditSettings
        {
            Geometry = new GeometrySettings { Vertical = 17, Horizontal = -23, Aspect = 11, Distortion = -9 }
        }),
        new("raw-profile", new EditSettings
        {
            RawProfile = new RawProfileSelection
            {
                Source = RawProfileSource.UserFile,
                Location = "sync-parity-profile.dcp",
                ContentHash = new string('a', 64)
            }
        }),
        new("lens", new EditSettings
        {
            Lens = new LensSettings { ProfileOverride = "Sync parity lens", Distortion = false }
        }),
        new("preset", new EditSettings { Exposure = -.65, AppliedPresetId = PresetId }),
        new("deleted-preset", new EditSettings { Contrast = -31, AppliedPresetId = DeletedPresetId })
    ];

    // LK is shared conceptually with the existing G2 fixture; the model test checks equality.
    internal static EditSettings CreateLook()
    {
        var settings = new EditSettings
        {
            Exposure = .75,
            Brightness = 11,
            Contrast = 23,
            Highlights = -37,
            Shadows = 29,
            Whites = 17,
            Blacks = -13,
            Saturation = 19,
            Vibrance = 31,
            Wb = new WhiteBalanceSettings { Mode = WbMode.Custom, Kelvin = 7200, Tint = -12 },
            HlReconstruction = HlReconstructionMode.Blend,
            Detail = new DetailSettings { CaptureSharpen = 41, LuminanceNr = 27, ChromaNr = 33 },
            Effects = new EffectsSettings
            {
                Vignette = -21,
                Midpoint = 63,
                Grain = 19,
                GrainSize = GrainSize.Coarse
            },
            Lens = new LensSettings { Distortion = false, ChromaticAberration = false, Vignetting = true },
            Mixer = new ColorMixerSettings(),
            Curve = Curve(.25, .19),
            CurveRed = Curve(.35, .43),
            CurveGreen = Curve(.45, .39),
            CurveBlue = Curve(.65, .71)
        };

        foreach (var band in Enum.GetValues<ColorMixerBand>())
        {
            var values = settings.Mixer.GetBand(band);
            values.Hue = 12;
            values.Saturation = -18;
            values.Luminance = 24;
        }

        return settings;
    }

    private static CurveData Curve(double x, double y)
    {
        var curve = new CurveData();
        curve.AddPointAndReturnIndex(x, y);

        return curve;
    }
}
