using Avalonia.Media;
using HappyPhoton.Models;

namespace HappyPhoton.Views;

/// <summary>
/// Code-behind twin of Themes/HappyPhotonTheme.axaml for controls that create brushes in C#.
/// Happy Photon design source: docs/DESIGN.md.
/// </summary>
public static class HappyPhotonColors
{
    public static readonly IBrush Primary = Brush("#dbfcff");
    public static readonly IBrush PrimaryContainer = Brush("#00f0ff");

    public static readonly IBrush BurstCyan = Brush("#00f0ff");
    public static readonly IBrush BurstMagenta = Brush("#ff24e4");
    public static readonly IBrush BurstPurple = Brush("#a06bff");
    public static readonly IBrush BurstIce = Brush("#7df4ff");
    public static readonly IBrush BurstPink = Brush("#fface8");
    public static readonly IBrush BurstViolet = Brush("#d1bcff");

    // Every burst hue must keep >= 4.5:1 contrast against this chip ink.
    public static readonly IBrush BurstChipInk = Brush("#0e0e13");

    public static readonly IBrush MidGrayBurstCyan = Brush("#00dbe9");
    public static readonly IBrush MidGrayBurstMagenta = Brush("#ff6de7");
    public static readonly IBrush MidGrayBurstPurple = Brush("#bda0ff");
    public static readonly IBrush MidGrayBurstIce = Brush("#a8f8ff");
    public static readonly IBrush MidGrayBurstPink = Brush("#ffd7f0");
    public static readonly IBrush MidGrayBurstViolet = Brush("#e1d2ff");

    public static readonly IBrush ColorLabelRed = Brush("#e34b4b");
    public static readonly IBrush ColorLabelYellow = Brush("#e5c85a");
    public static readonly IBrush ColorLabelGreen = Brush("#66c27a");
    public static readonly IBrush ColorLabelBlue = Brush("#4a7ce6");
    public static readonly IBrush ColorLabelPurple = Brush("#a77ad9");

    public static readonly IBrush WaveformTrace = Brush("#cfe6e8");
    public static readonly IBrush WaveformBackdrop = Brush("#1b1b20");
    public static readonly IBrush MidGrayWaveformBackdrop = Brush("#3d3d3d");

    public static readonly IBrush HistogramRed = Argb(120, 0xff, 0x6b, 0x7a);
    public static readonly IBrush HistogramGreen = Argb(120, 0x7d, 0xf4, 0xd1);
    public static readonly IBrush HistogramBlue = Argb(120, 0x72, 0x6f, 0xff);
    public static readonly IBrush HistogramLuminance = Argb(190, 0xdb, 0xfc, 0xff);

    public static readonly IBrush CurveControlPoint = Brush("#ffffffff");
    public static readonly IBrush CurveNormalStroke = Brush("#849495");
    public static readonly IBrush CurveReferenceLine = Brush("#3cffffff");
    public static readonly IBrush CurveGridLine = Brush("#1effffff");
    public static readonly IBrush CropMask = Brush("#a0000000");
    public static readonly IBrush CropBorder = Brush("#ffffffff");
    public static readonly IBrush CropHandleFill = Brush("#ffffffff");
    public static readonly IBrush CropHandleStroke = Brush("#505050");
    public static readonly IBrush CropGridLine = Brush("#64ffffff");

    private static IBrush Brush(string value) => new SolidColorBrush(Color.Parse(value));

    public static IBrush GetColorLabelBrush(ColorLabel label) => label switch
    {
        ColorLabel.Red => ColorLabelRed,
        ColorLabel.Yellow => ColorLabelYellow,
        ColorLabel.Green => ColorLabelGreen,
        ColorLabel.Blue => ColorLabelBlue,
        ColorLabel.Purple => ColorLabelPurple,
        _ => Brushes.Transparent
    };

    private static IBrush Argb(byte alpha, byte red, byte green, byte blue) =>
        new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
}
