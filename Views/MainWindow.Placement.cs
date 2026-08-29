using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using HappyPhoton.Services;

namespace HappyPhoton.Views;

public partial class MainWindow
{
    private WindowPlacementStore? _windowPlacementStore;
    private (PixelPoint Position, Size Size, double Scaling) _normalBounds;
    private bool _hasNormalBounds;
    private bool _normalBoundsTrackingPending;

    internal void RestoreWindowPlacement(
        WindowPlacementStore store,
        WindowPlacement? saved,
        IReadOnlyList<WindowPlacementScreen>? testScreens = null)
    {
        _windowPlacementStore = store;
        PositionChanged += (_, _) => QueueNormalWindowBoundsTracking();
        SizeChanged += (_, _) => QueueNormalWindowBoundsTracking();
        var resolved = WindowPlacement.Resolve(
            saved,
            testScreens ?? Screens.All.Select(ToPlacementScreen).ToArray(),
            (MinWidth, MinHeight));
        if (resolved == null)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(
            checked((int)Math.Round(resolved.X)),
            checked((int)Math.Round(resolved.Y)));
        Width = resolved.Width;
        Height = resolved.Height;
        _normalBounds = (
            Position, new Size(resolved.Width, resolved.Height), resolved.Scaling);
        _hasNormalBounds = true;
        if (resolved.Maximized) WindowState = WindowState.Maximized;
    }

    internal WindowPlacement CaptureWindowPlacement()
    {
        var position = _hasNormalBounds ? _normalBounds.Position : Position;
        var size = _hasNormalBounds ? _normalBounds.Size : ClientSize;
        return new WindowPlacement(
            WindowPlacement.CurrentVersion,
            position.X,
            position.Y,
            size.Width,
            size.Height,
            _hasNormalBounds ? _normalBounds.Scaling : RenderScaling,
            WindowState == WindowState.Maximized ||
            (WindowState == WindowState.FullScreen &&
             _windowStateBeforeFullScreen == WindowState.Maximized));
    }

    private void QueueNormalWindowBoundsTracking()
    {
        if (_normalBoundsTrackingPending) return;
        _normalBoundsTrackingPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _normalBoundsTrackingPending = false;
            TrackNormalWindowBounds();
        });
    }

    private void TrackNormalWindowBounds()
    {
        if (WindowState != WindowState.Normal ||
            ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        _normalBounds = (Position, ClientSize, RenderScaling);
        _hasNormalBounds = true;
    }

    private void SaveWindowPlacement()
    {
        TrackNormalWindowBounds();
        _windowPlacementStore?.Save(CaptureWindowPlacement());
    }

    private static WindowPlacementScreen ToPlacementScreen(Screen screen) =>
        new(screen.Bounds, screen.WorkingArea, screen.Scaling);
}
