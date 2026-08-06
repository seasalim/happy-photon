using HappyPhoton.Models;

namespace HappyPhoton.Tests;

public sealed record GoldenSettingsCase(
    string Slug,
    Func<EditSettings> CreateSettings);

public sealed record GoldenAssetCase(
    string Slug,
    string FileName,
    bool IsRaw,
    bool IsHeic,
    IReadOnlyList<GoldenSettingsCase> SettingsCases)
{
    public string FilePath => Path.Combine(GoldenTestPaths.AssetDirectory, FileName);
}

internal static class GoldenTestCases
{
    public static readonly GoldenSettingsCase Identity =
        new("identity", () => new EditSettings());

    public static readonly GoldenSettingsCase ExposurePlus2 =
        new("exposure-plus-2", () => new EditSettings { Exposure = 2 });

    private static readonly GoldenSettingsCase ExposureMinus2 =
        new("exposure-minus-2", () => new EditSettings { Exposure = -2 });

    private static readonly GoldenSettingsCase HighlightsMinus100 =
        new("highlights-minus-100", () => new EditSettings { Highlights = -100 });

    private static readonly GoldenSettingsCase ShadowsPlus80 =
        new("shadows-plus-80", () => new EditSettings { Shadows = 80 });

    private static readonly GoldenSettingsCase ContrastPlus50 =
        new("contrast-plus-50", () => new EditSettings { Contrast = 50 });

    private static readonly GoldenSettingsCase FullCombo =
        new("full-combo-tonal", CreateFullCombo);

    private static readonly GoldenSettingsCase WhiteBalance3000 =
        new("wb-3000", () => CreateWhiteBalance(3000, 0));

    private static readonly GoldenSettingsCase WhiteBalance9000TintPlus50 =
        new("wb-9000-tint-plus-50", () => CreateWhiteBalance(9000, 50));

    private static readonly GoldenSettingsCase WhiteBalance9000TintMinus50 =
        new("wb-9000-tint-minus-50", () => CreateWhiteBalance(9000, -50));

    private static readonly IReadOnlyList<GoldenSettingsCase> AllTonal =
    [
        Identity,
        ExposurePlus2,
        ExposureMinus2,
        HighlightsMinus100,
        ShadowsPlus80,
        ContrastPlus50,
        FullCombo
    ];

    private static readonly IReadOnlyList<GoldenSettingsCase> AllCases =
    [
        .. AllTonal,
        WhiteBalance3000,
        WhiteBalance9000TintPlus50,
        WhiteBalance9000TintMinus50
    ];

    private static readonly IReadOnlyList<GoldenSettingsCase> IdentityAndExposure =
    [
        Identity,
        ExposurePlus2
    ];

    private static readonly IReadOnlyList<GoldenSettingsCase> IdentityExposureAndWb =
    [
        .. IdentityAndExposure,
        WhiteBalance3000
    ];

    public static IReadOnlyList<GoldenAssetCase> Assets { get; } =
    [
        new("canon-eos-350d", "canon-eos-350d.cr2", true, false, AllCases),
        new("display-p3-reference", "display-p3-reference.jpg", false, false, AllCases),
        new("nikon-d70", "nikon-d70-burst-1.nef", true, false, IdentityExposureAndWb),
        new("fujifilm-x30", "fujifilm-x30.raf", true, false, IdentityExposureAndWb),
        new("pentax-k-r", "pentax-k-r.dng", true, false, IdentityExposureAndWb),
        new("adobe-rgb-reference", "adobe-rgb-reference.jpg", false, false, IdentityExposureAndWb),
        new("srgb-reference", "srgb-reference.jpg", false, false, IdentityExposureAndWb),
        new("reference-16bit", "reference-16bit.tiff", false, false, IdentityExposureAndWb),
        new("reference-heic", "reference.heic", false, true, [Identity])
    ];

    private static EditSettings CreateFullCombo()
    {
        var curve = new CurveData();
        curve.AddPointAndReturnIndex(0.25, 0.20);
        curve.AddPointAndReturnIndex(0.75, 0.82);
        return new EditSettings
        {
            Exposure = 1,
            Brightness = 10,
            Contrast = 25,
            Shadows = 35,
            Highlights = -50,
            Curve = curve
        };
    }

    private static EditSettings CreateWhiteBalance(double kelvin, double tint) =>
        new()
        {
            Wb = new WhiteBalanceSettings
            {
                Mode = WbMode.Custom,
                Kelvin = kelvin,
                Tint = tint
            }
        };
}
