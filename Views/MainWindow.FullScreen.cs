using Avalonia.Controls;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class MainWindow
{
    private WindowState _windowStateBeforeFullScreen = WindowState.Normal;

    private ZoomPanControl? GetActiveZoomPanControl()
    {
        if (DataContext is MainWindowViewModel { IsFullScreenMode: true })
        {
            return _fullScreenZoomPanControl;
        }

        return _zoomPanControl;
    }

    private void ApplyFullScreenWindowState(bool isFullScreen)
    {
        if (isFullScreen)
        {
            if (WindowState != WindowState.FullScreen)
            {
                _windowStateBeforeFullScreen = WindowState;
            }

            WindowState = WindowState.FullScreen;
            return;
        }

        if (WindowState == WindowState.FullScreen)
        {
            WindowState = _windowStateBeforeFullScreen == WindowState.FullScreen
                ? WindowState.Normal
                : _windowStateBeforeFullScreen;
        }
    }
}
