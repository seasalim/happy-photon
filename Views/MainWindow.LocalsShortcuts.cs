using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class MainWindow
{
    private void InitializeLocalsShortcuts()
    {
        AddHandler(KeyUpEvent, OnLocalsKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        Deactivated += (_, _) => ClearLocalsHeldKeys();
        LostFocus += (_, _) =>
        {
            if (FocusManager?.GetFocusedElement() is not Visual focused || GetTopLevel(focused) != this)
                ClearLocalsHeldKeys();
        };
    }

    private void ClearLocalsHeldKeys() => WithVm(vm =>
    {
        vm.IsLocalMaskHeld = false;
        vm.IsBrushAltHeld = false;
    });

    private bool TryHandleLocalsKey(KeyEventArgs e, MainWindowViewModel vm)
    {
        if (WorkspaceKeyRouting.IsEnterTextInputFocused(FocusManager?.GetFocusedElement()))
            return false;
        if (e.Key == Key.B && e.KeyModifiers == KeyModifiers.None && vm.CanArmBrushFromShortcut)
        {
            e.Handled = true;
            _ = ArmBrushFromShortcutAsync(vm);
            return true;
        }
        if (e.Key == Key.W && e.KeyModifiers == KeyModifiers.Shift &&
            vm.IsDevelopMode && !vm.IsFullScreenMode && vm.HasSelectedImage)
        {
            if (vm.ToggleLocalsModeCommand.CanExecute(null))
                vm.ToggleLocalsModeCommand.Execute(null);
            e.Handled = true;
            return true;
        }
        if (!vm.IsLocalsMode || vm.IsLocalHuePicking || vm.IsWhiteBalancePicking) return false;
        if (vm.IsBrushSectionVisible && vm.CanEditLocals)
        {
            if (e.Key is Key.LeftAlt or Key.RightAlt)
                vm.IsBrushAltHeld = true;
            else if (e.Key is Key.OemOpenBrackets or Key.OemCloseBrackets &&
                e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift)
                vm.StepBrushPreference(e.Key == Key.OemCloseBrackets, e.KeyModifiers == KeyModifiers.Shift);
            else return HandleMaskKey(e, vm);
            e.Handled = true;
            return true;
        }
        return HandleMaskKey(e, vm);
    }

    private static async Task ArmBrushFromShortcutAsync(MainWindowViewModel vm)
    {
        try
        {
            var image = vm.SelectedImage;
            if (!vm.IsLocalsMode)
            {
                if (!vm.ToggleLocalsModeCommand.CanExecute(null)) return;
                await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
            }
            if (ReferenceEquals(image, vm.SelectedImage) && vm.CanArmBrushFromShortcut && vm.AddBrushCommand.CanExecute(null))
                vm.AddBrushCommand.Execute(null);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Brush shortcut failed: {exception}");
        }
    }

    private static bool HandleMaskKey(KeyEventArgs e, MainWindowViewModel vm)
    {
        if (e.KeyModifiers != KeyModifiers.None) return false;
        if (e.Key == Key.O) vm.ShowLocalMask = !vm.ShowLocalMask;
        else if (e.Key == Key.M) vm.IsLocalMaskHeld = true;
        else return false;
        e.Handled = true;
        return true;
    }

    private void OnLocalsKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.M) WithVm(vm => vm.IsLocalMaskHeld = false);
        if (e.Key is Key.LeftAlt or Key.RightAlt) WithVm(vm => vm.IsBrushAltHeld = false);
    }
}
