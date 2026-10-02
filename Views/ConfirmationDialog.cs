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
        Title = title;
        Width = 420;
        MinWidth = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        Classes.Add("dialog");

        var cancel = CreateButton(cancelLabel, false);
        var confirm = CreateButton(confirmLabel, true);
        var actions = new StackPanel { Classes = { "dialog-buttons" } };

        if (buttons == ConfirmationDialogButtons.YesNo)
        {
            actions.Children.Add(cancel);
            confirm.Classes.Add(destructive ? "destructive" : "accent");
        }

        actions.Children.Add(confirm);
        var footer = new Border { Classes = { "dialog-footer" }, Child = actions };
        Grid.SetRow(footer, 1);
        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children =
            {
                new Border
                {
                    Classes = { "dialog-content" },
                    Child = new StackPanel
                    {
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock { Text = title, Classes = { "dialog-title" } },
                            new TextBlock { Text = message, Classes = { "dialog-body" } }
                        }
                    }
                },
                footer
            }
        };
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
        var button = new Button { Content = label, Classes = { "quiet-button" } };
        button.Click += (_, _) => Close(result);

        return button;
    }
}
