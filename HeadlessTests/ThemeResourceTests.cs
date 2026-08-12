using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ThemeResourceTests
{
    public static TheoryData<ThemeVariant> Variants => new()
    {
        ThemeVariant.Dark,
        HappyPhotonThemes.MidGrey
    };

    [AvaloniaFact]
    public void AssessmentGrey_IsInvariantAndMatchesShippedMidGreySurround()
    {
        var dark = Brush("AssessmentGrey", ThemeVariant.Dark).Color;
        var mid = Brush("AssessmentGrey", HappyPhotonThemes.MidGrey).Color;
        var surround = Brush(
            "ViewerSurround",
            HappyPhotonThemes.MidGrey).Color;

        Assert.Equal(Color.Parse("#777777"), dark);
        Assert.Equal(dark, mid);
        Assert.Equal(mid, surround);
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
