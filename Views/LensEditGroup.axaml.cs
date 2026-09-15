using Avalonia.Controls;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class LensEditGroup : UserControl
{
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
