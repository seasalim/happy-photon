using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using HappyPhoton.Views;

namespace HappyPhoton.Converters;

/// <summary>Maps a burst color index to one of six Happy Photon hues.</summary>
public class BurstColorConverter : IMultiValueConverter
{
    private static readonly IBrush[] DarkPalette =
    {
        Views.HappyPhotonColors.BurstCyan,
        Views.HappyPhotonColors.BurstMagenta,
        Views.HappyPhotonColors.BurstPurple,
        Views.HappyPhotonColors.BurstIce,
        Views.HappyPhotonColors.BurstPink,
        Views.HappyPhotonColors.BurstViolet,
    };

    private static readonly IBrush[] MidGrayPalette =
    {
        HappyPhotonColors.MidGrayBurstCyan,
        HappyPhotonColors.MidGrayBurstMagenta,
        HappyPhotonColors.MidGrayBurstPurple,
        HappyPhotonColors.MidGrayBurstIce,
        HappyPhotonColors.MidGrayBurstPink,
        HappyPhotonColors.MidGrayBurstViolet,
    };

    public object Convert(
        IList<object?> values,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        var palette = values.Count > 1 &&
                      values[1] is ThemeVariant theme &&
                      theme == HappyPhotonThemes.MidGray
            ? MidGrayPalette
            : DarkPalette;
        var index = values.Count > 0 && values[0] is int value && value >= 0
            ? value
            : 0;
        return palette[index % palette.Length];
    }
}
