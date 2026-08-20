using Avalonia.Controls;
using Avalonia.Data.Converters;
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
    }

    private async void OnCurveChanged(object? sender, EventArgs e) =>
        await ForwardCurveChangedAsync();

    private void OnCurveEditStarted(object? sender, EventArgs e) =>
        (DataContext as MainWindowViewModel)?.OnCurveEditStarted();

    internal Task ForwardCurveChangedAsync() =>
        DataContext is MainWindowViewModel viewModel
            ? viewModel.OnCurveChangedAsync()
            : Task.CompletedTask;
}
