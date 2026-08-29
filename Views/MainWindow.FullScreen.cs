using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class MainWindow
{
    private WindowState _windowStateBeforeFullScreen = WindowState.Normal;
    private TimeProvider _fullScreenExitTimeProvider = TimeProvider.System;
    private ITimer? _fullScreenExitTimer;
    private long _fullScreenExitRevealSerial;
    private bool _isFullScreenExitCleanupHooked;

    internal TimeProvider FullScreenExitTimeProvider
    {
        get => _fullScreenExitTimeProvider;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            StopFullScreenExitTimer();
            _fullScreenExitTimeProvider = value;
        }
    }

    internal bool IsFullScreenExitTimerActive => _fullScreenExitTimer != null;

    private ZoomPanControl? GetActiveZoomPanControl()
    {
        if (DataContext is MainWindowViewModel { IsFullScreenMode: true })
        {
            return _fullScreenZoomPanControl;
        }

        if (DataContext is MainWindowViewModel { IsLoupeMode: true })
        {
            return _loupeView?.Viewer;
        }

        return _zoomPanControl;
    }

    private void ApplyFullScreenWindowState(bool isFullScreen)
    {
        HideFullScreenExitButton();
        StopFullScreenExitTimer();
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

    // The zoom-pan control marks pointer moves handled while panning or holding
    // the loupe, so the chip listens on the tunnel with handled moves included:
    // dragging the photograph must still be able to reveal the way out.
    private void HookFullScreenExitReveal() =>
        FullScreenOverlay.AddHandler(
            PointerMovedEvent,
            OnFullScreenOverlayPointerMoved,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);

    private void OnFullScreenOverlayPointerMoved(
        object? sender,
        PointerEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { IsFullScreenMode: true })
        {
            return;
        }

        EnsureFullScreenExitCleanup();
        FullScreenExitButton.Opacity = 1;
        FullScreenExitButton.IsHitTestVisible = true;
        StopFullScreenExitTimer();
        var serial = ++_fullScreenExitRevealSerial;
        _fullScreenExitTimer = FullScreenExitTimeProvider.CreateTimer(
            _ => Dispatcher.UIThread.Post(() => FadeFullScreenExitButton(serial)),
            null,
            TimeSpan.FromSeconds(2),
            Timeout.InfiniteTimeSpan);
    }

    private void FadeFullScreenExitButton(long serial)
    {
        if (serial != _fullScreenExitRevealSerial ||
            DataContext is not MainWindowViewModel { IsFullScreenMode: true })
        {
            return;
        }

        HideFullScreenExitButton();
        StopFullScreenExitTimer();
    }

    private void HideFullScreenExitButton()
    {
        FullScreenExitButton.Opacity = 0;
        FullScreenExitButton.IsHitTestVisible = false;
        _fullScreenExitRevealSerial++;
    }

    private void StopFullScreenExitTimer()
    {
        _fullScreenExitTimer?.Dispose();
        _fullScreenExitTimer = null;
    }

    private void EnsureFullScreenExitCleanup()
    {
        if (_isFullScreenExitCleanupHooked) return;

        _isFullScreenExitCleanupHooked = true;
        Closed += OnFullScreenWindowClosed;
    }

    private void OnFullScreenWindowClosed(object? sender, EventArgs e)
    {
        HideFullScreenExitButton();
        StopFullScreenExitTimer();
        Closed -= OnFullScreenWindowClosed;
        _isFullScreenExitCleanupHooked = false;
    }
}
