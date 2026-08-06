using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace HappyPhoton.Views;

public enum ConfirmationDialogButtons
{
    Ok,
    YesNo
}

public partial class ConfirmationDialog : Window
{
    private readonly ConfirmationDialogButtons _buttons;
    private readonly bool _destructive;

    private ConfirmationDialog(string title, string message, ConfirmationDialogButtons buttons, bool destructive)
    {
        _buttons = buttons;
        _destructive = destructive;

        Title = title;
        Width = 420;
        MinWidth = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        Background = HappyPhotonColors.SurfaceLow;

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    Margin = new Thickness(20, 20, 20, 16),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = HappyPhotonColors.TextPrimary,
                    FontSize = 13
                },
                CreateButtons()
            }
        };
    }

    public static async Task<bool> ConfirmAsync(
        Window owner,
        string title,
        string message,
        bool destructive = false)
    {
        var dialog = new ConfirmationDialog(title, message, ConfirmationDialogButtons.YesNo, destructive);
        return await dialog.ShowDialog<bool>(owner);
    }

    public static Task ShowMessageAsync(Window owner, string title, string message)
    {
        var dialog = new ConfirmationDialog(title, message, ConfirmationDialogButtons.Ok, destructive: false);
        return dialog.ShowDialog(owner);
    }

    private StackPanel CreateButtons()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(20, 0, 20, 20)
        };

        Grid.SetRow(panel, 1);

        if (_buttons == ConfirmationDialogButtons.YesNo)
        {
            panel.Children.Add(CreateButton("No", false));
            panel.Children.Add(CreateButton("Yes", true));
        }
        else
        {
            panel.Children.Add(CreateButton("OK", true));
        }

        return panel;
    }

    private Button CreateButton(string label, bool result)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = 80,
            Padding = new Thickness(16, 6),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };

        if (result && _destructive)
        {
            button.Classes.Add("destructive");
        }
        else if (result)
        {
            button.Classes.Add("accent");
        }

        button.Click += (_, _) => Close(result);
        return button;
    }
}
