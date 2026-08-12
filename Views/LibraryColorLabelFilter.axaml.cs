using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HappyPhoton.Models;

namespace HappyPhoton.Views;

public partial class LibraryColorLabelFilter : UserControl
{
    public static readonly StyledProperty<ColorLabelFilter> FilterProperty =
        AvaloniaProperty.Register<LibraryColorLabelFilter, ColorLabelFilter>(
            nameof(Filter),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<IReadOnlyList<ColorLabelFilterChoice>>
        ChoicesProperty = AvaloniaProperty.Register<
            LibraryColorLabelFilter,
            IReadOnlyList<ColorLabelFilterChoice>>(nameof(Choices));

    public ColorLabelFilter Filter
    {
        get => GetValue(FilterProperty);
        set => SetValue(FilterProperty, value);
    }

    public IReadOnlyList<ColorLabelFilterChoice> Choices
    {
        get => GetValue(ChoicesProperty);
        set => SetValue(ChoicesProperty, value);
    }

    public LibraryColorLabelFilter()
    {
        InitializeComponent();
        Choices = [];
    }

    private void OnFilterClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ColorLabelFilter requested }) return;
        Filter = Filter == requested ? ColorLabelFilter.All : requested;
    }
}
