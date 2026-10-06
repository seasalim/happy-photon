using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

// BRAND-WP1: the brand colour lives in one theme key, the welcome heading and the
// icon rasterised from Assets/happy-photon-icon.svg. The expected values are the
// spec's Design-table numbers for the chosen candidate.
public sealed partial class BrandMarkTests
{
    private const string BrandKey = "Brand";

    private const string BrandColorKey = "BrandColor";

    private const string BrandHex = "#3aa6b9";

    private const string DarkMonoDiscHex = "#d2d2d2";

    private const string MidGrayMonoDiscHex = "#ececec";

    private const double DarkHeadingContrast = 5.99;

    private const double MidGrayHeadingContrast = 3.79;

    private const double DiscOnDarkTaskbarContrast = 5.69;

    private static readonly int[] IcoFrameSizes = [16, 24, 32, 48, 64, 128, 256];

    private static string AssetPath(string name) =>
        Path.Combine(GoldenTestPaths.RepositoryRoot, "Assets", name);

    [AvaloniaTheory]
    [InlineData(false, DarkHeadingContrast, 4.5)]
    [InlineData(true, MidGrayHeadingContrast, 3.0)]
    public async Task WelcomeHeading_MeetsItsContrastOnTheCard(bool midgray, double expected, double minimum)
    {
        await using var scene = new WelcomeScene();
        var variant = midgray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark;
        using var themeScope = new TestUiScope(scene.Window, variant);

        var heading = scene.Heading();
        var card = heading.GetLogicalAncestors().OfType<Border>().First(border => border.Background != null);
        var ratio = ThemeResourceTests.Contrast(
            Assert.IsAssignableFrom<ISolidColorBrush>(heading.Foreground).Color,
            Assert.IsAssignableFrom<ISolidColorBrush>(card.Background).Color);

        Assert.InRange(ratio, expected - 0.05, expected + 0.05);
        Assert.True(ratio >= minimum, $"Welcome heading contrast {ratio:F2}:1 is under {minimum}:1.");
        Assert.Equal(Color.Parse(BrandHex), ThemeResourceTests.Brush(BrandKey, variant).Color);
    }

    [AvaloniaTheory]
    [InlineData(256, 3.0)]
    [InlineData(32, 6.0)]
    [InlineData(16, 6.0)]
    public void IconRaster_CentreIsTheBrandColourWithTransparentCorners(int size, double maximumDeltaE)
    {
        using var raster = Rasterise(size);
        var pixels = Rgba(raster);

        var centre = Pixel(pixels, size, size / 2, size / 2);
        Assert.Equal(255, centre.A);

        var deltaE = DeltaE(centre, Color.Parse(BrandHex));
        Assert.True(deltaE <= maximumDeltaE, $"Centre at {size} px is ΔE {deltaE:F2} from {BrandHex}.");

        foreach (var (x, y) in new[] { (0, 0), (size - 1, 0), (0, size - 1), (size - 1, size - 1) })
        {
            Assert.Equal(0, Pixel(pixels, size, x, y).A);
        }

        if (size == 256)
        {
            var ratio = ThemeResourceTests.Contrast(centre, Color.Parse("#202020"));

            Assert.InRange(ratio, DiscOnDarkTaskbarContrast - 0.1, DiscOnDarkTaskbarContrast + 0.1);
            Assert.True(ratio >= 3.0, $"Icon disc on a dark taskbar is {ratio:F2}:1.");
        }
    }

    [AvaloniaFact]
    public void CommittedPng_MatchesAFreshRasterOfTheSvg()
    {
        using var committed = new MagickImage(AssetPath("happy-photon-icon.png"));

        AssertMatchesFreshRaster(committed, "happy-photon-icon.png");
    }

    [AvaloniaFact]
    public void CommittedIco_HasEveryFrameAndEachMatchesAFreshRaster()
    {
        using var frames = new MagickImageCollection(AssetPath("happy-photon-icon.ico"));

        Assert.Equal(IcoFrameSizes, frames.Select(frame => (int)frame.Width).Order().ToArray());

        foreach (var frame in frames)
        {
            Assert.Equal(frame.Width, frame.Height);
            AssertMatchesFreshRaster(frame, $"happy-photon-icon.ico {frame.Width} px");
        }
    }

    [AvaloniaFact]
    public void IconTileStroke_MatchesTheDarkOutlineVariant()
    {
        var svg = File.ReadAllText(AssetPath("happy-photon-icon.svg"));
        var stroke = System.Text.RegularExpressions.Regex.Match(svg, "<rect[^>]*stroke=\"(#[0-9a-fA-F]{6})\"");

        Assert.True(stroke.Success, "The icon tile has no stroke.");
        Assert.Equal(
            ThemeResourceTests.Brush("OutlineVariant", ThemeVariant.Dark).Color,
            Color.Parse(stroke.Groups[1].Value));
    }

    [AvaloniaFact]
    public void BrandHex_LivesOnlyInTheBrandKey()
    {
        var theme = File.ReadAllText(Path.Combine(GoldenTestPaths.RepositoryRoot, "Themes", "HappyPhotonTheme.axaml"));
        var colors = File.ReadAllText(Path.Combine(GoldenTestPaths.RepositoryRoot, "Views", "HappyPhotonColors.cs"));

        Assert.Equal(1, Occurrences(theme, BrandHex));
        Assert.Contains($"<Color x:Key=\"{BrandColorKey}\">{BrandHex}</Color>", theme, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(BrandHex == "#00f0ff" ? 1 : 0, Occurrences(colors, BrandHex));
    }

    private static int Occurrences(string text, string hex) =>
        System.Text.RegularExpressions.Regex.Matches(
            text, System.Text.RegularExpressions.Regex.Escape(hex),
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;

    private static void AssertMatchesFreshRaster(IMagickImage<ushort> committed, string what)
    {
        var size = (int)committed.Width;
        using var fresh = Rasterise(size);
        var expected = Rgba(fresh);
        var actual = Rgba(committed);
        var worstAlpha = 0;
        var worst = 0.0;

        for (var index = 0; index < expected.Length; index += 4)
        {
            worstAlpha = Math.Max(worstAlpha, Math.Abs(actual[index + 3] - expected[index + 3]));
            worst = Math.Max(worst, DeltaE(OverTaskbar(actual, index), OverTaskbar(expected, index)));
        }

        Assert.True(worstAlpha <= 2 && worst <= 1.0,
            $"{what} differs from a fresh raster of the SVG: alpha by {worstAlpha}, ΔE {worst:F2} over a dark taskbar.");
    }

    internal static MagickImage Rasterise(int size)
    {
        var settings = new MagickReadSettings
        {
            Width = (uint)size,
            Height = (uint)size,
            BackgroundColor = MagickColors.Transparent
        };

        return new MagickImage(AssetPath("happy-photon-icon.svg"), settings);
    }

    internal static byte[] Rgba(IMagickImage<ushort> image) =>
        image.GetPixelsUnsafe().ToByteArray(PixelMapping.RGBA) ??
        throw new InvalidOperationException("Could not read the raster's pixels.");

    internal static Color Pixel(byte[] rgba, int width, int x, int y)
    {
        var index = (y * width + x) * 4;

        return Color.FromArgb(rgba[index + 3], rgba[index], rgba[index + 1], rgba[index + 2]);
    }

    private static Color OverTaskbar(byte[] rgba, int index)
    {
        var alpha = rgba[index + 3] / 255d;
        byte Blend(byte channel) => (byte)Math.Round(channel * alpha + 0x20 * (1 - alpha));

        return Color.FromRgb(Blend(rgba[index]), Blend(rgba[index + 1]), Blend(rgba[index + 2]));
    }

    internal static double DeltaE(Color first, Color second) =>
        PrecisionDeltaE.FromSrgb(
            first.R / 255d, first.G / 255d, first.B / 255d,
            second.R / 255d, second.G / 255d, second.B / 255d);
}
