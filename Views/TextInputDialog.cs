using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace HappyPhoton.Views;

public class TextInputDialog : Window
{
    private readonly TextBox _textBox;
    private readonly Button _okButton;

    private TextInputDialog(string title, string prompt, string initialText)
    {
        Title = title;
        Width = 420;
        MinWidth = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        Background = HappyPhotonColors.SurfaceLow;

        _textBox = new TextBox
        {
            Text = initialText,
            Margin = new Thickness(20, 0, 20, 16)
        };
        _textBox.TextChanged += (_, _) => UpdateOkState();

        _okButton = CreateButton("OK");
        _okButton.Classes.Add("accent");
        _okButton.Click += (_, _) => Confirm();

        var cancelButton = CreateButton("Cancel");
        cancelButton.Click += (_, _) => Close(null);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(20, 0, 20, 20),
            Children = { cancelButton, _okButton }
        };
        Grid.SetRow(buttons, 2);

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            Children =
            {
                new TextBlock
                {
                    Text = prompt,
                    Margin = new Thickness(20, 20, 20, 8),
                    Foreground = HappyPhotonColors.TextPrimary,
                    FontSize = 13
                },
                _textBox,
                buttons
            }
        };
        Grid.SetRow(_textBox, 1);

        KeyDown += OnKeyDown;
        Opened += (_, _) =>
        {
            _textBox.Focus();
            _textBox.SelectAll();
        };
        UpdateOkState();
    }

    public static Task<string?> ShowAsync(
        Window owner,
        string title,
        string prompt,
        string initialText = "")
    {
        return new TextInputDialog(title, prompt, initialText).ShowDialog<string?>(owner);
    }

    private static Button CreateButton(string label)
    {
        return new Button
        {
            Content = label,
            MinWidth = 80,
            Padding = new Thickness(16, 6),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
    }

    private void UpdateOkState()
    {
        _okButton.IsEnabled = !string.IsNullOrWhiteSpace(_textBox.Text);
    }

    private void Confirm()
    {
        if (!string.IsNullOrWhiteSpace(_textBox.Text))
        {
            Close(_textBox.Text.Trim());
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _okButton.IsEnabled)
        {
            Confirm();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
    }
}
