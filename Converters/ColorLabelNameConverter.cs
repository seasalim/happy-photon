using System.Globalization;
using Avalonia.Data.Converters;
using HappyPhoton.Models;

namespace HappyPhoton.Converters;

public sealed class ColorLabelNameConverter : IMultiValueConverter
{
    public object? Convert(
        IList<object?> values,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        if (values.Count < 2 || values[0] is not ColorLabel label ||
            label == ColorLabel.None)
        {
            return null;
        }

        return values[1] is IEnumerable<ColorLabelChoice> choices
            ? choices.FirstOrDefault(choice => choice.Value == label)?.Name ??
              label.ToString()
            : label.ToString();
    }
}
