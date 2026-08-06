using Avalonia.Input;

namespace HappyPhoton.Views;

public partial class MainWindow
{
    private List<KeyBinding>? _suspendedWorkspaceKeyBindings;
    private bool _workspaceKeyboardEnabled = true;

    internal bool WorkspaceKeyboardEnabled => _workspaceKeyboardEnabled;

    private void ApplyWorkspaceKeyboardState(bool isEnabled)
    {
        if (_workspaceKeyboardEnabled == isEnabled)
        {
            return;
        }

        _workspaceKeyboardEnabled = isEnabled;
        if (!isEnabled)
        {
            _suspendedWorkspaceKeyBindings = KeyBindings.ToList();
            KeyBindings.Clear();
            return;
        }

        if (_suspendedWorkspaceKeyBindings == null)
        {
            return;
        }

        foreach (var keyBinding in _suspendedWorkspaceKeyBindings)
        {
            KeyBindings.Add(keyBinding);
        }
        _suspendedWorkspaceKeyBindings = null;
    }
}
