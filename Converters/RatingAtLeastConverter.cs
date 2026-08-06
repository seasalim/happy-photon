using System.Globalization;
using Avalonia.Data.Converters;

namespace HappyPhoton.Converters;

public class RatingAtLeastConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int rating &&
        int.TryParse(parameter?.ToString(), out var threshold) &&
        rating >= threshold;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
