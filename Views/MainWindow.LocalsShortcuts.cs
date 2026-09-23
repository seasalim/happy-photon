using Avalonia.Input;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class MainWindow
{
    private bool TryHandleLocalsKey(KeyEventArgs e, MainWindowViewModel vm)
    {
        if (WorkspaceKeyRouting.IsEnterTextInputFocused(FocusManager?.GetFocusedElement()))
            return false;
        if (e.Key == Key.W && e.KeyModifiers == KeyModifiers.Shift &&
            vm.IsDevelopMode && !vm.IsFullScreenMode && vm.HasSelectedImage)
        {
            if (vm.ToggleLocalsModeCommand.CanExecute(null))
                vm.ToggleLocalsModeCommand.Execute(null);
            e.Handled = true;
            return true;
        }
        if (!vm.IsLocalsMode || vm.IsLocalHuePicking || vm.IsWhiteBalancePicking ||
            e.KeyModifiers != KeyModifiers.None) return false;
        if (e.Key == Key.O) vm.ShowLocalMask = !vm.ShowLocalMask;
        else if (e.Key == Key.M) vm.IsLocalMaskHeld = true;
        else return false;
        e.Handled = true;
        return true;
    }

    private void OnLocalsKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.M) WithVm(vm => vm.IsLocalMaskHeld = false);
    }
}
