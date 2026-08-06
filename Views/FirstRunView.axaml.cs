using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class FirstRunView : UserControl
{
    public FirstRunView()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty && change.GetNewValue<bool>())
        {
            Dispatcher.UIThread.Post(FocusInitialAction, DispatcherPriority.Input);
        }
    }

    private void FocusInitialAction()
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (vm.HasDefaultFirstRunLocation)
        {
            StartInDefaultButton.Focus();
        }
        else
        {
            ChooseLocationButton.Focus();
        }
    }
}
