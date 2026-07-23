using Avalonia.Media;

namespace HappyPhoton.Views;

/// <summary>
/// Code-behind twin of Themes/HappyPhotonTheme.axaml for controls that create brushes in C#.
/// Happy Photon design source: docs/DESIGN.md.
/// </summary>
public static class HappyPhotonColors
{
    public static readonly IBrush SurfaceLow = Brush("#1b1b20");
    public static readonly IBrush SurfaceHighest = Brush("#35343a");
    public static readonly IBrush Outline = Brush("#849495");
    public static readonly IBrush TextPrimary = Brush("#e4e1e9");
    public static readonly IBrush Primary = Brush("#dbfcff");
    public static readonly IBrush PrimaryContainer = Brush("#00f0ff");
    public static readonly IBrush SecondaryContainer = Brush("#ff24e4");
    public static readonly IBrush Tertiary = Brush("#e1d2ff");
    public static readonly IBrush ErrorContainer = Brush("#93000a");
    public static readonly IBrush OnErrorContainer = Brush("#ffdad6");

    public static readonly IBrush BurstCyan = Brush("#00f0ff");
    public static readonly IBrush BurstMagenta = Brush("#ff24e4");
    public static readonly IBrush BurstPurple = Brush("#7213ff");
    public static readonly IBrush BurstIce = Brush("#7df4ff");
    public static readonly IBrush BurstPink = Brush("#fface8");
    public static readonly IBrush BurstViolet = Brush("#d1bcff");

    public static readonly IBrush HistogramRed = Argb(120, 0xff, 0x6b, 0x7a);
    public static readonly IBrush HistogramGreen = Argb(120, 0x7d, 0xf4, 0xd1);
    public static readonly IBrush HistogramBlue = Argb(120, 0x72, 0x6f, 0xff);
    public static readonly IBrush HistogramLuminance = Argb(190, 0xdb, 0xfc, 0xff);

    private static IBrush Brush(string value) => new SolidColorBrush(Color.Parse(value));

    private static IBrush Argb(byte alpha, byte red, byte green, byte blue) =>
        new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
}
