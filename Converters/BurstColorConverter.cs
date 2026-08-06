using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace HappyPhoton.Converters;

/// <summary>Maps a burst color index to one of six Happy Photon hues.</summary>
public class BurstColorConverter : IValueConverter
{
    private static readonly IBrush[] Palette =
    {
        Views.HappyPhotonColors.BurstCyan,
        Views.HappyPhotonColors.BurstMagenta,
        Views.HappyPhotonColors.BurstPurple,
        Views.HappyPhotonColors.BurstIce,
        Views.HappyPhotonColors.BurstPink,
        Views.HappyPhotonColors.BurstViolet,
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int index && index >= 0 ? Palette[index % Palette.Length] : Palette[0];

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
