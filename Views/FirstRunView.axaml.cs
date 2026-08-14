using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class FirstRunView : UserControl
{
    private MainWindowViewModel? _subscribedViewModel;

    public FirstRunView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_subscribedViewModel != null)
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribedViewModel = DataContext as MainWindowViewModel;
        if (_subscribedViewModel != null)
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        base.OnDataContextChanged(e);
        QueueFocus();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && change.GetNewValue<bool>())
            QueueFocus();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.FirstRunStep) ||
            e.PropertyName == nameof(MainWindowViewModel.StartupGateState))
        {
            QueueFocus();
        }
    }

    private void QueueFocus() => Dispatcher.UIThread.Post(
        FocusPrimaryAction,
        DispatcherPriority.Input);

    private void FocusPrimaryAction()
    {
        if (DataContext is not MainWindowViewModel vm || !vm.IsFirstRunVisible) return;
        switch (vm.FirstRunStep)
        {
            case FirstRunStep.Welcome:
                WelcomeContinueButton.Focus();
                break;
            case FirstRunStep.Storage:
                StorageContinueButton.Focus();
                break;
            case FirstRunStep.Pictures when vm.HasDefaultFirstRunLocation:
                PicturesDefaultButton.Focus();
                break;
            case FirstRunStep.Pictures:
                PicturesChooseButton.Focus();
                break;
            case FirstRunStep.Lightroom:
                LightroomImportButton.Focus();
                break;
            case FirstRunStep.AllSet:
                StartTourButton.Focus();
                break;
        }
    }
}
