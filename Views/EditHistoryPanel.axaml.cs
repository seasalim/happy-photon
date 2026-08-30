using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class EditHistoryPanel : UserControl
{
    public EditHistoryPanel()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnHistoryRowPointerPressed,
            RoutingStrategies.Tunnel);
    }

    private void OnHistoryRowPointerPressed(
        object? sender,
        PointerPressedEventArgs e)
    {
        var source = e.Source as Control;
        var button = source as Button ?? source?.FindAncestorOfType<Button>();
        if (button?.DataContext is not EditHistoryEntry entry ||
            !e.GetCurrentPoint(button).Properties.IsLeftButtonPressed ||
            !e.KeyModifiers.HasFlag(KeyModifiers.Alt) ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }
        e.Handled = true;
        _ = viewModel.ClearHistoryAboveStepCommand.ExecuteAsync(entry);
    }
}

public static class EditHistoryLayoutConverters
{
    public static IValueConverter FortyPercent { get; } =
        new FuncValueConverter<double, double>(value => value * 0.4);
}
