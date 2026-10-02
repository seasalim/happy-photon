using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace HappyPhoton.Views;

public partial class CompactSlider
{
    private static readonly GridLength PanelLabelWidth = new(70);

    private static readonly GridLength PanelValueWidth = new(40);

    public static readonly StyledProperty<bool> IsInlineProperty =
        AvaloniaProperty.Register<CompactSlider, bool>(nameof(IsInline));

    // Inline sliders stand alone in a bar, so their label and value columns fit their own text
    // instead of the panel columns that line up stacked sliders.
    public bool IsInline
    {
        get => GetValue(IsInlineProperty);
        set => SetValue(IsInlineProperty, value);
    }

    private void OnInlinePropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == IsInlineProperty ||
            IsInline && (change.Property == MinimumProperty ||
                         change.Property == MaximumProperty ||
                         change.Property == StringFormatProperty))
        {
            UpdateInlineColumns();
        }
    }

    private void UpdateInlineColumns()
    {
        if (_layoutGrid == null || _valueText == null) return;

        var columns = _layoutGrid.ColumnDefinitions;

        if (!IsInline)
        {
            columns[0].Width = PanelLabelWidth;
            columns[2].Width = PanelValueWidth;

            return;
        }

        // The widest of the range's ends keeps the track still as the value's digit count changes.
        var valueWidth = Math.Max(MeasureValueText(Minimum), MeasureValueText(Maximum));
        columns[0].Width = GridLength.Auto;
        columns[2].Width = new GridLength(Math.Ceiling(valueWidth));
    }

    private double MeasureValueText(double value)
    {
        var typeface = new Typeface(_valueText!.FontFamily, _valueText.FontStyle, _valueText.FontWeight);
        using var layout = new TextLayout(string.Format(StringFormat, value), typeface, _valueText.FontSize, null);

        return layout.WidthIncludingTrailingWhitespace;
    }
}
