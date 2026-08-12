using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using HappyPhoton.Models;

namespace HappyPhoton.Converters;

public sealed class ColorLabelBrushConverter : IValueConverter
{
    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) => Views.HappyPhotonColors.GetColorLabelBrush(value switch
        {
            ColorLabel label => label,
            ColorLabelFilter filter when filter != ColorLabelFilter.All =>
                (ColorLabel)((int)filter - 1),
            _ => ColorLabel.None
        });

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) => throw new NotSupportedException();
}
