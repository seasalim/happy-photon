using Avalonia.Input;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class MainWindow
{
    private bool TryHandleSpotsKey(KeyEventArgs e, MainWindowViewModel vm)
    {
        if (WorkspaceKeyRouting.IsEnterTextInputFocused(FocusManager?.GetFocusedElement()) ||
            e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift))
            return false;
        if (e.KeyModifiers == KeyModifiers.Shift && e.Key is not (Key.OemOpenBrackets or Key.OemCloseBrackets)) return false;

        if (e.Key == Key.Q && vm.IsDevelopMode && !vm.IsFullScreenMode && vm.HasSelectedImage)
            vm.ToggleSpotsModeCommand.Execute(null);
        else if (vm.IsSpotsMode && e.Key is Key.Delete or Key.Back)
            vm.DeleteSpotCommand.Execute(null);
        else if (!vm.CanUseSpotShortcuts) return false;
        else if (e.Key == Key.A) vm.VisualizeSpots = !vm.VisualizeSpots;
        else if (e.Key == Key.H) vm.HideSpotCircles = !vm.HideSpotCircles;
        else if (e.Key == Key.OemQuestion) vm.NextSpotSourceCommand.Execute(null);
        else if (e.Key is Key.OemOpenBrackets or Key.OemCloseBrackets)
            vm.StepSpotPreferenceCommand.Execute((e.KeyModifiers == KeyModifiers.Shift ? "feather" : "size") +
                (e.Key == Key.OemCloseBrackets ? "+" : "-"));
        else return false;

        e.Handled = true;

        return true;
    }
}
