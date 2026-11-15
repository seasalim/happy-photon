using Avalonia.Controls;
using Avalonia.Input;

namespace HappyPhoton.Views;

public class TextInputDialog : Window
{
    private readonly TextBox _textBox;
    private readonly Button _okButton;

    internal TextInputDialog(string title, string prompt, string initialText)
    {
        _textBox = new TextBox
        {
            Text = initialText,
            Classes = { "edit-field" }
        };
        _textBox.TextChanged += (_, _) => UpdateOkState();

        _okButton = DialogChrome.Button("OK");
        _okButton.Classes.Add("accent");
        _okButton.Click += (_, _) => Confirm();

        var cancelButton = DialogChrome.Button("Cancel");
        cancelButton.Click += (_, _) => Close(null);

        DialogChrome.Build(this, title, prompt, _textBox, cancelButton, _okButton);

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
