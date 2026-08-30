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
        new(value => value.ToString().ToUpperInvariant());

    public DevelopEditPanel()
    {
        InitializeComponent();
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

    private void OnSliderDragStarted(object? sender, RoutedEventArgs e) =>
        (DataContext as MainWindowViewModel)?.OnSliderEditStarted();

    private void OnSliderDragCompleted(object? sender, RoutedEventArgs e) =>
        (DataContext as MainWindowViewModel)?.OnSliderEditCompleted();

    internal Task ForwardCurveChangedAsync() =>
        DataContext is MainWindowViewModel viewModel
            ? viewModel.OnCurveChangedAsync()
            : Task.CompletedTask;
}
