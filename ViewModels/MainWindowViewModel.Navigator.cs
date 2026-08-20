using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private Rect? _navigatorVisibleRegion;

    internal void PublishNavigatorVisibleRegion(Rect? region)
    {
        NavigatorVisibleRegion = IsDevelopPreviewSurfaceActive
            ? region
            : null;
    }

    private void ClearNavigatorVisibleRegion() =>
        NavigatorVisibleRegion = null;

    private void UpdateNavigatorPreviewSurfaceActivity()
    {
        if (!IsDevelopPreviewSurfaceActive)
        {
            ClearNavigatorVisibleRegion();
        }
    }
}
