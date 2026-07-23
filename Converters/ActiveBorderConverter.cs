using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace HappyPhoton.Converters;

public class ActiveBorderConverter : IValueConverter
{
    public static readonly ActiveBorderConverter Instance = new();

    private static readonly IBrush ActiveBrush = Views.HappyPhotonColors.PrimaryContainer;
    private static readonly IBrush TransparentBrush = Brushes.Transparent;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? ActiveBrush : TransparentBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
