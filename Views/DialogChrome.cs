using Avalonia.Controls;

namespace HappyPhoton.Views;

/// <summary>The fixed-width chrome shared by the code-built dialogs.</summary>
internal static class DialogChrome
{
    /// <summary>Sets the window up and lays out title, body, optional input and the button row.</summary>
    public static void Build(Window dialog, string title, string body, Control? input, params Button[] buttons)
    {
        dialog.Title = title;
        dialog.Width = 420;
        dialog.MinWidth = 320;
        dialog.SizeToContent = SizeToContent.Height;
        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        dialog.CanResize = false;
        dialog.Classes.Add("dialog");

        var text = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = title, Classes = { "dialog-title" } },
                new TextBlock { Text = body, Classes = { "dialog-body" } }
            }
        };
        if (input != null) text.Children.Add(input);

        var actions = new StackPanel { Classes = { "dialog-buttons" } };
        actions.Children.AddRange(buttons);
        var footer = new Border { Classes = { "dialog-footer" }, Child = actions };
        Grid.SetRow(footer, 1);
        dialog.Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children =
            {
                new Border { Classes = { "dialog-content" }, Child = text },
                footer
            }
        };
    }

    public static Button Button(string label) => new() { Content = label, Classes = { "quiet-button" } };
}
