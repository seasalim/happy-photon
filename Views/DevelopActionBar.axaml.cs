using Avalonia.Controls;
using Avalonia.Input;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class DevelopActionBar : UserControl
{
    public DevelopActionBar()
    {
        InitializeComponent();
    }

    private void OnPasteContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            !vm.ChoosePasteSettingsCommand.CanExecute(null))
        {
            return;
        }

        e.Handled = true;
        vm.ChoosePasteSettingsCommand.Execute(null);
    }
}
