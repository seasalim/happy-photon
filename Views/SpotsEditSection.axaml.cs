using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class SpotsEditSection : UserControl
{
    private MainWindowViewModel? _sliderOwner;

    private IPointer? _sliderPointer;

    public SpotsEditSection()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, (_, e) => _sliderPointer = e.Pointer, RoutingStrategies.Tunnel);
        AddHandler(CompactSlider.DragStartedEvent, (_, e) =>
        {
            if (DataContext is MainWindowViewModel vm && e.Source is CompactSlider slider && slider.Label != "Threshold" &&
                vm.BeginSpotSliderEdit("Spot " + slider.Label.ToLowerInvariant()))
            {
                _sliderOwner = vm;
                vm.PropertyChanged += SliderOwnerChanged;
            }
            e.Handled = true;
        });
        AddHandler(CompactSlider.DragCompletedEvent, async (_, e) =>
        {
            var owner = ReleaseSlider();
            e.Handled = true;
            if (owner != null) await owner.CompleteSpotSliderEditAsync();
        });
        DataContextChanged += (_, _) => CancelSlider();
        DetachedFromVisualTree += (_, _) => CancelSlider();
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

    private void SliderOwnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsSpotsGestureActive) &&
            _sliderOwner?.IsSpotsGestureActive == false) ReleaseSlider();
    }

    private MainWindowViewModel? ReleaseSlider()
    {
        var owner = _sliderOwner;
        _sliderOwner = null;
        if (owner != null) owner.PropertyChanged -= SliderOwnerChanged;
        var pointer = _sliderPointer;
        _sliderPointer = null;
        if (pointer?.Captured is Avalonia.Visual captured && captured.GetVisualAncestors().Contains(this))
            pointer.Capture(null);
        return owner;
    }

    private void CancelSlider() => ReleaseSlider()?.DiscardSpotsGesture();
}
