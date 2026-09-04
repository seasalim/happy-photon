using Avalonia;
using Avalonia.Platform;
using Avalonia.Threading;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class MainWindow
{
    private readonly DispatcherTimer _displayProfileTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(100),
    };

    private int _displayProfileAttempts;

    private void InitializeDisplayColorManagement()
    {
        _displayProfileTimer.Tick += OnDisplayProfileTimerTick;
        Opened += (_, _) => QueueDisplayProfileResolution();
        PositionChanged += (_, _) => QueueDisplayProfileResolution();
        if (OperatingSystem.IsMacOS())
        {
            // The Metal layer can be recreated with the backing scale.
            ScalingChanged += (_, _) => QueueDisplayProfileResolution();
        }
        Closed += (_, _) =>
        {
            _displayProfileTimer.Stop();
            _displayProfileTimer.Tick -= OnDisplayProfileTimerTick;
        };
    }

    // On macOS the Metal layer exists only after the first frame has rendered, so the
    // timer keeps ticking until the layer is tagged, bounded by the ViewModel.
    private void QueueDisplayProfileResolution()
    {
        _displayProfileAttempts = 0;
        _displayProfileTimer.Stop();
        _displayProfileTimer.Start();
    }

    private void OnDisplayProfileTimerTick(object? sender, EventArgs e)
    {
        _displayProfileTimer.Stop();
        ResolveDisplayProfileNow();
    }

    private void ResolveDisplayProfileNow()
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            TryGetPlatformHandle() is not { } handle)
        {
            return;
        }

        var nativeHandle = handle is IMacOSTopLevelPlatformHandle mac
            ? mac.NSView
            : handle.Handle;
        viewModel.ResolveDisplayProfile(nativeHandle);
        if (OperatingSystem.IsMacOS() &&
            MainWindowViewModel.ShouldRetryMacOsDisplayProfile(
                viewModel.DisplayTransform.Support,
                ++_displayProfileAttempts))
        {
            _displayProfileTimer.Start();
        }
    }
}
