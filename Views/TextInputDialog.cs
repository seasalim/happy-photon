using Avalonia.Controls;
using Avalonia.Input;

namespace HappyPhoton.Views;

public class TextInputDialog : Window
{
    private readonly TextBox _textBox;
    private readonly Button _okButton;

    internal TextInputDialog(string title, string prompt, string initialText)
    {
        Title = title;
        Width = 420;
        MinWidth = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        Classes.Add("dialog");

        _textBox = new TextBox
        {
            Text = initialText,
            Classes = { "edit-field" }
        };
        _textBox.TextChanged += (_, _) => UpdateOkState();

        _okButton = CreateButton("OK");
        _okButton.Classes.Add("accent");
        _okButton.Click += (_, _) => Confirm();

        var cancelButton = CreateButton("Cancel");
        cancelButton.Click += (_, _) => Close(null);

        var footer = new Border
        {
            Classes = { "dialog-footer" },
            Child = new StackPanel
            {
                Classes = { "dialog-buttons" },
                Children = { cancelButton, _okButton }
            }
        };
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
                            new TextBlock { Text = prompt, Classes = { "dialog-body" } },
                            _textBox
                        }
                    }
                },
                footer
            }
        };

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
            Classes = { "quiet-button" }
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
