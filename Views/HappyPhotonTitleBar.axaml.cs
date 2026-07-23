using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace HappyPhoton.Views;

public partial class HappyPhotonTitleBar : UserControl
{
    public HappyPhotonTitleBar()
    {
        InitializeComponent();
    }

    private Window? OwnerWindow => this.FindAncestorOfType<Window>();

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            e.Source is Control control && control.FindAncestorOfType<Button>() != null)
        {
            return;
        }

        if (OwnerWindow is { WindowState: WindowState.Normal } window)
        {
            window.BeginMoveDrag(e);
        }
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        if (OwnerWindow is { } window)
        {
            window.WindowState = WindowState.Minimized;
        }
    }

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
    {
        if (OwnerWindow is { } window)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        OwnerWindow?.Close();
    }
}
