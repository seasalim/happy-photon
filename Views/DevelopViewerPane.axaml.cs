using Avalonia.Controls;

namespace HappyPhoton.Views;

public partial class DevelopViewerPane : UserControl
{
    public DevelopViewerPane()
    {
        InitializeComponent();
    }

    public ZoomPanControl Viewer => ZoomPanControl;
}
