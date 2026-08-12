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
    private readonly string _cancelLabel;
    private readonly string _confirmLabel;

    internal ConfirmationDialog(
        string title,
        string message,
        ConfirmationDialogButtons buttons,
        bool destructive,
        string cancelLabel = "No",
        string confirmLabel = "Yes")
    {
        _buttons = buttons;
        _destructive = destructive;
        _cancelLabel = cancelLabel;
        _confirmLabel = confirmLabel;

        Title = title;
        Width = 420;
        MinWidth = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        Background = ResolveBrush("SurfaceLow");

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
                    Foreground = ResolveBrush("TextPrimary"),
                    FontSize = 13
                },
                CreateButtons()
            }
        };
    }

    private IBrush ResolveBrush(string key)
    {
        if (this.TryFindResource(key, ActualThemeVariant, out var resource) &&
            resource is IBrush brush)
        {
            return brush;
        }

        throw new InvalidOperationException($"Theme brush '{key}' was not found.");
    }

    public static async Task<bool> ConfirmAsync(
        Window owner,
        string title,
        string message,
        bool destructive = false,
        string cancelLabel = "No",
        string confirmLabel = "Yes")
    {
        var dialog = new ConfirmationDialog(
            title,
            message,
            ConfirmationDialogButtons.YesNo,
            destructive,
            cancelLabel,
            confirmLabel);
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
            panel.Children.Add(CreateButton(_cancelLabel, false));
            panel.Children.Add(CreateButton(_confirmLabel, true));
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
