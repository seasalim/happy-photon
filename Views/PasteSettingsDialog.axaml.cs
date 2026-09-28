using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class PasteSettingsDialog : Window
{
    public PasteSettingsDialog()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnDialogKeyDown, RoutingStrategies.Tunnel);
    }

    public PasteSettingsDialog(PasteSettingsViewModel model) : this()
    {
        DataContext = model;
    }

    private void OnPaste(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PasteSettingsViewModel { CanPaste: true }) Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void OnDialogKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape)) return;

        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            Close(false);
        }
        else
        {
            OnPaste(sender, e);
        }
    }
}
