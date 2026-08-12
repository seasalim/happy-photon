using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using HappyPhoton.Models;

namespace HappyPhoton.Converters;

public sealed class ColorLabelOutlineConverter : IMultiValueConverter
{
    public object Convert(
        IList<object?> values,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        values.Count >= 2 &&
        values[0] is Enum active &&
        values[1] is Enum candidate &&
        active.GetType() == candidate.GetType() &&
        active.Equals(candidate)
            ? Views.HappyPhotonColors.Primary
            : parameter as IBrush ?? Brushes.Transparent;
}
