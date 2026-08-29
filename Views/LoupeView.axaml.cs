using Avalonia.Controls;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class LoupeView : UserControl
{
    public ZoomPanControl Viewer { get; }

    public LoupeView()
    {
        InitializeComponent();
        Viewer = this.FindControl<ZoomPanControl>("LoupeZoomPanControl")!;
        Viewer.ZoomChanged += (_, delta) => ViewModel?.AdjustZoom(delta);
        Viewer.AutoFitRequested += (_, zoom) => ViewModel?.ApplyFitZoom(zoom);
        Viewer.RequiredDeviceLongEdgeChanged += (_, longEdge) =>
            ViewModel?.PublishLoupeRequiredDeviceLongEdge(
                longEdge,
                Viewer.IsLoupePeekActive);
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    public bool CancelLoupePeek() => Viewer.CancelLoupePeek();
}
