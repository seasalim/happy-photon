using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ThemeResourceTests
{
    public static TheoryData<ThemeVariant> Variants => new()
    {
        ThemeVariant.Dark,
        HappyPhotonThemes.MidGray
    };

    [AvaloniaFact]
    public void AssessmentGray_IsInvariantAndMatchesShippedMidGraySurround()
    {
        var dark = Brush("AssessmentGray", ThemeVariant.Dark).Color;
        var mid = Brush("AssessmentGray", HappyPhotonThemes.MidGray).Color;
        var surround = Brush(
            "ViewerSurround",
            HappyPhotonThemes.MidGray).Color;

        Assert.Equal(Color.Parse("#777777"), dark);
        Assert.Equal(dark, mid);
        Assert.Equal(mid, surround);
    }

    [AvaloniaFact]
    public void AssessmentWhite_IsInvariantAndKeepsOneBrushInstance()
    {
        var dark = Brush("AssessmentWhite", ThemeVariant.Dark);
        var mid = Brush("AssessmentWhite", HappyPhotonThemes.MidGray);

        Assert.Equal(Color.Parse("#FFFFFF"), dark.Color);
        Assert.Same(dark, mid);
    }

    [AvaloniaTheory]
    [MemberData(nameof(Variants))]
    public void Theme_TextSelectionAndFocusPairsMeetContrastTargets(
        ThemeVariant variant)
    {
        AssertContrast(4.5, "TextPrimary", "SurfaceHigh", variant);
        AssertContrast(4.5, "TextSecondary", "SurfaceLow", variant);
        AssertContrast(4.5, "TextMuted", "SurfaceLowest", variant);
        AssertContrast(4.5, "TextPrimary", "SelectionSurface", variant);
        AssertContrast(3, "PrimaryContainer", "SurfaceBright", variant);
        AssertContrast(3, "ActiveImageRing", "SelectionSurface", variant);
        AssertContrast(3, "SelectionCheck", "SelectionSurface", variant);
    }

    [AvaloniaFact]
    public void RejectMark_MeetsContrastTargetOnControlBarBackground()
    {
        AssertContrast(3, "RejectMark", "ViewerSurround", ThemeVariant.Dark);
        AssertContrast(3, "RejectMark", "SurfaceLow", HappyPhotonThemes.MidGray);
    }

    [AvaloniaTheory]
    [MemberData(nameof(Variants))]
    public void Theme_BrandAccentPairsMeetContrastTargets(ThemeVariant variant)
    {
        AssertContrast(4.5, "OnBrandAccent", "BrandAccent", variant);
        AssertContrast(4.5, "OnBrandAccent", "BrandAccentHover", variant);
        AssertContrast(3, "BrandAccent", "SurfaceLowest", variant);
    }

    [AvaloniaFact]
    public void Dark_BrandAccentTokensPreserveExistingPalette()
    {
        Assert.Equal(
            Resource<Color>("PrimaryContainerColor", ThemeVariant.Dark),
            Brush("BrandAccent", ThemeVariant.Dark).Color);
        Assert.Equal(
            Resource<Color>("PrimaryHoverColor", ThemeVariant.Dark),
            Brush("BrandAccentHover", ThemeVariant.Dark).Color);
        Assert.Equal(
            Resource<Color>("OnPrimaryColor", ThemeVariant.Dark),
            Brush("OnBrandAccent", ThemeVariant.Dark).Color);
    }

    [AvaloniaFact]
    public void MidGray_ChromeNeutralsAreStrictlyAchromatic()
    {
        foreach (var key in new[]
        {
            "SurfaceLowest", "SurfaceBase", "SurfaceLow", "SurfaceMid",
            "SurfaceHigh", "SurfaceHighest", "SurfaceBright",
            "Outline", "OutlineVariant", "TextPrimary", "TextSecondary",
            "TextMuted", "TextDisabled", "RawFileBackground",
            "ViewerSurround", "FullScreenBackdrop", "SelectionSurface",
            "BrandAccent", "BrandAccentHover", "OnBrandAccent",
            "ActiveImageRing"
        })
        {
            var color = Brush(key, HappyPhotonThemes.MidGray).Color;
            Assert.True(
                color.R == color.G && color.G == color.B,
                $"{key} resolved to {color}, which carries a color cast.");
        }
    }

    // Asserted through the BrandMark resource rather than by opening the asset
    // by name, so the theme dictionary is pinned to an asset of the right
    // character. Checking the rendered pixels rather than a file path also keeps
    // the test honest if the mark is ever redrawn or renamed.
    [AvaloniaFact]
    public void BrandMark_ResolvesToAChromaticMarkOnlyUnderDark()
    {
        Assert.False(
            HasChroma(HappyPhotonThemes.MidGray),
            "The Middle Gray brand mark carries a color cast.");
        Assert.True(
            HasChroma(ThemeVariant.Dark),
            "The Dark brand mark lost its cyan.");
    }

    private static bool HasChroma(ThemeVariant variant)
    {
        var mark = Resource<ImageBrush>("BrandMark", variant);
        var bitmap = Assert.IsType<Bitmap>(mark.Source);

        using var encoded = new MemoryStream();
        bitmap.Save(encoded);
        encoded.Position = 0;

        using var image = new MagickImage(encoded);
        var pixels = image.GetPixelsUnsafe().ToByteArray(PixelMapping.RGBA) ??
            throw new InvalidOperationException(
                $"Could not read the {variant} brand mark.");

        for (var index = 0; index < pixels.Length; index += 4)
        {
            if (pixels[index + 3] == 0)
            {
                continue;
            }

            if (pixels[index] != pixels[index + 1] ||
                pixels[index + 1] != pixels[index + 2])
            {
                return true;
            }
        }

        return false;
    }

    [AvaloniaTheory]
    [MemberData(nameof(Variants))]
    public void Theme_DimmedTourAndDisabledContentRemainDistinguishable(
        ThemeVariant variant)
    {
        var surround = Brush("ViewerSurround", variant).Color;
        var dimmedOpacity = Resource<double>("TourDimmedOpacity", variant);
        var dimmedText = Composite(
            Brush("TextPrimary", variant).Color,
            surround,
            dimmedOpacity);
        var dimmedSurface = Composite(
            Brush("SurfaceLow", variant).Color,
            surround,
            dimmedOpacity);
        Assert.True(Contrast(dimmedText, dimmedSurface) >= 2.5);

        var disabledOpacity = Resource<double>("DisabledOpacity", variant);
        var disabledText = Composite(
            Brush("TextDisabled", variant).Color,
            Brush("SurfaceLow", variant).Color,
            disabledOpacity);
        Assert.True(
            Contrast(disabledText, Brush("SurfaceLow", variant).Color) >= 1.5);

        AssertContrast(4.5, "TextPrimary", "SurfaceHigh", variant);
        AssertContrast(4.5, "TextSecondary", "SurfaceHigh", variant);
        Assert.InRange(Resource<double>("TourFocusGlowOpacity", variant), 0.5, 1);
        Assert.InRange(Resource<double>("CoachmarkFocusGlowOpacity", variant), 0.55, 1);
        Assert.NotNull(Resource<object>("CoachmarkShadow", variant));
    }

    [AvaloniaFact]
    public void BurstPalette_KeepsChipInkReadableOnEveryHue()
    {
        var ink = Assert.IsType<SolidColorBrush>(HappyPhotonColors.BurstChipInk).Color;
        foreach (var hue in new[]
        {
            HappyPhotonColors.BurstCyan,
            HappyPhotonColors.BurstMagenta,
            HappyPhotonColors.BurstPurple,
            HappyPhotonColors.BurstIce,
            HappyPhotonColors.BurstPink,
            HappyPhotonColors.BurstViolet,
            HappyPhotonColors.MidGrayBurstCyan,
            HappyPhotonColors.MidGrayBurstMagenta,
            HappyPhotonColors.MidGrayBurstPurple,
            HappyPhotonColors.MidGrayBurstIce,
            HappyPhotonColors.MidGrayBurstPink,
            HappyPhotonColors.MidGrayBurstViolet,
        })
        {
            var color = Assert.IsType<SolidColorBrush>(hue).Color;
            var contrast = Contrast(ink, color);
            Assert.True(
                contrast >= 4.5,
                $"Burst chip ink on {color} resolved to {contrast:F2}:1.");
        }
    }

    [AvaloniaFact]
    public void Dark_ControlSpecificOpacitiesPreserveExistingRendering()
    {
        Assert.Equal(0.5, Resource<double>("TourFocusGlowOpacity", ThemeVariant.Dark));
        Assert.Equal(0.55, Resource<double>("CoachmarkFocusGlowOpacity", ThemeVariant.Dark));
        Assert.Equal(0.32, Resource<double>("DisabledOpacity", ThemeVariant.Dark));
    }

    private static void AssertContrast(
        double minimum,
        string foreground,
        string background,
        ThemeVariant variant)
    {
        var actual = Contrast(
            Brush(foreground, variant).Color,
            Brush(background, variant).Color);
        Assert.True(
            actual >= minimum,
            $"{foreground} on {background} resolved to {actual:F2}:1.");
    }

    internal static SolidColorBrush Brush(string key, ThemeVariant variant) =>
        Assert.IsType<SolidColorBrush>(Resource<object>(key, variant));

    internal static T Resource<T>(string key, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryGetResource(key, variant, out var value));
        return Assert.IsAssignableFrom<T>(value);
    }

    internal static double Contrast(Color first, Color second)
    {
        var firstLuminance = Luminance(first);
        var secondLuminance = Luminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05) /
               (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    private static double Luminance(Color color) =>
        0.2126 * Linear(color.R) +
        0.7152 * Linear(color.G) +
        0.0722 * Linear(color.B);

    private static double Linear(byte channel)
    {
        var value = channel / 255d;
        return value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static Color Composite(Color foreground, Color background, double opacity)
    {
        return Color.FromRgb(
            Blend(foreground.R, background.R, opacity),
            Blend(foreground.G, background.G, opacity),
            Blend(foreground.B, background.B, opacity));
    }

    private static byte Blend(byte foreground, byte background, double opacity) =>
        (byte)Math.Round(foreground * opacity + background * (1 - opacity));
}
