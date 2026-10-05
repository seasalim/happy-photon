using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class CropEditSection : UserControl
{
    private void OnRatioPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && e.Source is Control source)
        {
            e.Handled = ActivateRatioItem(source);
        }
    }

    private void OnRatioKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space && e.Source is Control source)
        {
            e.Handled = ActivateRatioItem(source);
        }
    }

    private bool ActivateRatioItem(Control source)
    {
        var item = source as ComboBoxItem ?? source.FindAncestorOfType<ComboBoxItem>();
        if (!CropRatioPicker.IsDropDownOpen || item?.Content is not string name ||
            DataContext is not MainWindowViewModel vm)
        {
            return false;
        }

        // Activation must also apply the currently displayed, derived ratio.
        vm.ChooseCropRatio(name);
        CropRatioPicker.SetCurrentValue(ComboBox.SelectedItemProperty, vm.CropRatio);
        CropRatioPicker.IsDropDownOpen = false;

        return true;
    }

    private void OnRatioSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || sender is not ComboBox { SelectedItem: string name } picker ||
            name == vm.CropRatio)
        {
            return;
        }

        vm.ChooseCropRatio(name);
        picker.SetCurrentValue(ComboBox.SelectedItemProperty, vm.CropRatio);
    }

    private void OnAutoClick(object? sender, RoutedEventArgs e) =>
        this.FindAncestorOfType<Window>()?.Focus();

    public CropEditSection()
    {
        InitializeComponent();
        CropRatioPicker.AddHandler(PointerReleasedEvent, OnRatioPointerReleased, RoutingStrategies.Tunnel);
        CropRatioPicker.AddHandler(KeyDownEvent, OnRatioKeyDown, RoutingStrategies.Tunnel);

        // Horizon belongs to the crop draft, not the panel's slider transaction.
        AddHandler(CompactSlider.DragStartedEvent, (_, e) => e.Handled = true);
        AddHandler(CompactSlider.DragCompletedEvent, (_, e) => e.Handled = true);
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsVisible && this.FindAncestorOfType<ScrollViewer>() is { } scroll)
                        scroll.Offset = default;
                }, DispatcherPriority.Background);
        };
    }
}
