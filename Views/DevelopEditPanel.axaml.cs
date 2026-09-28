using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class DevelopEditPanel : UserControl
{
    public static FuncValueConverter<HlReconstructionMode, string>
        HighlightHandlingLabelConverter { get; } =
        new(value => value.ToString());

    public DevelopEditPanel()
    {
        InitializeComponent();
        AddHandler(CompactSlider.WheelInputStartedEvent, OnWheelInputStarted);
        AddHandler(CompactSlider.DragStartedEvent, OnSliderDragStarted);
        AddHandler(CompactSlider.DragCompletedEvent, OnSliderDragCompleted);
        PropertyChanged += (_, change) =>
        {
            if (change.Property == IsVisibleProperty) ResetScrollWhenShown();
        };
        var histogram = this.FindControl<HistogramView>("DevelopHistogram");
        histogram!.ClippingPeekStarted += (_, side) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.BeginClippingPeek(side);
            }
        };
        histogram.ClippingPeekEnded += (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.EndClippingPeek();
            }
        };
        histogram.ClippingLatchRequested += (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel &&
                viewModel.ToggleClippingOverlayCommand.CanExecute(null))
            {
                viewModel.ToggleClippingOverlayCommand.Execute(null);
            }
        };
    }

    private void ResetScrollWhenShown()
    {
        if (!IsVisible) return;

        Dispatcher.UIThread.Post(
            () =>
            {
                if (IsVisible) DevelopControlsScrollViewer.Offset = default;
            },
            DispatcherPriority.Background);
    }

    private async void OnCurveChanged(object? sender, EventArgs e) =>
        await ForwardCurveChangedAsync();

    private void OnCurveEditStarted(object? sender, EventArgs e) =>
        (DataContext as MainWindowViewModel)?.OnCurveEditStarted();

    private void OnWheelInputStarted(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not CompactSlider slider || DataContext is not MainWindowViewModel viewModel) return;

        viewModel.SliderEditsEnding += slider.CompleteWheel;
        slider.WheelInputEnded += OnWheelInputEnded;

        void OnWheelInputEnded()
        {
            viewModel.SliderEditsEnding -= slider.CompleteWheel;
            slider.WheelInputEnded -= OnWheelInputEnded;
        }
    }

    private void OnSliderDragStarted(object? sender, RoutedEventArgs e)
    {
        // A detached slider can no longer bubble its completion through this panel.
        if (e.Source is CompactSlider { IsWheelEditing: true } slider &&
            DataContext is MainWindowViewModel)
        {
            slider.DragCompleted += OnWheelDragCompleted;

            void OnWheelDragCompleted(object? completedSender, RoutedEventArgs completedArgs)
            {
                slider.DragCompleted -= OnWheelDragCompleted;

                OnSliderDragCompleted(completedSender, completedArgs);
                completedArgs.Handled = true;
            }
        }

        (DataContext as MainWindowViewModel)?.OnSliderEditStarted();
    }

    private void OnSliderDragCompleted(object? sender, RoutedEventArgs e) =>
        (DataContext as MainWindowViewModel)?.OnSliderEditCompleted(
            e.Source is CompactSlider slider && slider.Classes.Contains("local-geometry")
                ? "Local geometry" : e.Source is DualRangeTrack || e.Source is CompactSlider range && range.Classes.Contains("local-range")
                    ? "Luminance Range" : e.Source is CompactSlider hue && hue.Classes.Contains("local-hue") ? "Hue Range" : null,
            completeWheel: e is CompactSlider.WheelCompletedEventArgs);

    internal Task ForwardCurveChangedAsync() =>
        DataContext is MainWindowViewModel viewModel
            ? viewModel.OnCurveChangedAsync()
            : Task.CompletedTask;
}
