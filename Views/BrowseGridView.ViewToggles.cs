using Avalonia;

namespace HappyPhoton.Views;

public partial class BrowseGridView
{
    public static readonly StyledProperty<bool> ShowBurstsProperty =
        AvaloniaProperty.Register<BrowseGridView, bool>(
            nameof(ShowBursts),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<bool> ShowPairsProperty =
        AvaloniaProperty.Register<BrowseGridView, bool>(
            nameof(ShowPairs),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public bool ShowBursts
    {
        get => GetValue(ShowBurstsProperty);
        set => SetValue(ShowBurstsProperty, value);
    }

    public bool ShowPairs
    {
        get => GetValue(ShowPairsProperty);
        set => SetValue(ShowPairsProperty, value);
    }
}
