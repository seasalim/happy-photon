using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace HappyPhoton.Converters;

public class SelectionBackgroundConverter : IValueConverter
{
    public static readonly SelectionBackgroundConverter Instance = new();

    private static readonly IBrush SelectedBrush = Views.HappyPhotonColors.SurfaceHighest;
    private static readonly IBrush TransparentBrush = Brushes.Transparent;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? SelectedBrush : TransparentBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
