using Avalonia.Input;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class MainWindow
{
    private bool TryHandleSpotsKey(KeyEventArgs e, MainWindowViewModel vm)
    {
        if (WorkspaceKeyRouting.IsEnterTextInputFocused(FocusManager?.GetFocusedElement()) ||
            e.KeyModifiers != KeyModifiers.None) return false;
        if (e.Key == Key.Q && vm.IsDevelopMode && !vm.IsFullScreenMode && vm.HasSelectedImage)
            vm.ToggleSpotsModeCommand.Execute(null);
        else if (vm.IsSpotsMode && e.Key is Key.Delete or Key.Back)
            vm.DeleteSpotCommand.Execute(null);
        else return false;
        e.Handled = true;
        return true;
    }
}
