using Avalonia.Controls;
using Avalonia.Input;

namespace HappyPhoton.Views;

public enum ConfirmationDialogButtons
{
    Ok,
    YesNo
}

public partial class ConfirmationDialog : Window
{
    internal ConfirmationDialog(
        string title,
        string message,
        ConfirmationDialogButtons buttons,
        bool destructive,
        string cancelLabel,
        string confirmLabel)
    {
        var cancel = CreateButton(cancelLabel, false);
        var confirm = CreateButton(confirmLabel, true);

        if (buttons == ConfirmationDialogButtons.YesNo)
        {
            confirm.Classes.Add(destructive ? "destructive" : "accent");
            DialogChrome.Build(this, title, message, null, cancel, confirm);
        }
        else
        {
            DialogChrome.Build(this, title, message, null, confirm);
        }

        KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;

            Close(false);
            args.Handled = true;
        };
        Opened += (_, _) =>
            (destructive && buttons == ConfirmationDialogButtons.YesNo ? cancel : confirm).Focus(NavigationMethod.Tab);
    }

    public static async Task<bool> ConfirmAsync(
        Window owner,
        string title,
        string message,
        bool destructive,
        string cancelLabel,
        string confirmLabel)
    {
        var dialog = new ConfirmationDialog(
            title, message, ConfirmationDialogButtons.YesNo, destructive, cancelLabel, confirmLabel);

        return await dialog.ShowDialog<bool>(owner);
    }

    public static Task ShowMessageAsync(Window owner, string title, string message)
    {
        var dialog = new ConfirmationDialog(title, message, ConfirmationDialogButtons.Ok,
            destructive: false, cancelLabel: "Cancel", confirmLabel: "OK");

        return dialog.ShowDialog(owner);
    }

    private Button CreateButton(string label, bool result)
    {
        var button = DialogChrome.Button(label);
        button.Click += (_, _) => Close(result);

        return button;
    }
}
