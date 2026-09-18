using Avalonia.Controls;
using Avalonia.Data.Converters;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class LensEditGroup : UserControl
{
    // Keeps every dropdown row inside the picker's own width (row padding and
    // the scrollbar take the rest), so the popup never resizes while scrolling.
    public static IValueConverter ItemTextWidth { get; } =
        new FuncValueConverter<double, double>(width => Math.Max(0, width - 40));

    private void OnLensSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm &&
            sender is ComboBox { SelectedItem: string name })
            vm.SelectedLensName = name;
    }

    public LensEditGroup()
    {
        InitializeComponent();
    }
}
