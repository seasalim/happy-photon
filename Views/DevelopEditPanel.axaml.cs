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
    }

    private async void OnCurveChanged(object? sender, EventArgs e) =>
        await ForwardCurveChangedAsync();

    internal Task ForwardCurveChangedAsync() =>
        DataContext is MainWindowViewModel viewModel
            ? viewModel.OnCurveChangedAsync()
            : Task.CompletedTask;
}
