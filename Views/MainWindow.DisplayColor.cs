using Avalonia;
using Avalonia.Threading;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class MainWindow
{
    private readonly DispatcherTimer _displayProfileTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(100),
    };

    private void InitializeDisplayColorManagement()
    {
        _displayProfileTimer.Tick += OnDisplayProfileTimerTick;
        Opened += (_, _) => QueueDisplayProfileResolution();
        PositionChanged += (_, _) => QueueDisplayProfileResolution();
        Closed += (_, _) =>
        {
            _displayProfileTimer.Stop();
            _displayProfileTimer.Tick -= OnDisplayProfileTimerTick;
        };
    }

    private void QueueDisplayProfileResolution()
    {
        _displayProfileTimer.Stop();
        _displayProfileTimer.Start();
    }

    private void OnDisplayProfileTimerTick(object? sender, EventArgs e)
    {
        _displayProfileTimer.Stop();
        if (DataContext is not MainWindowViewModel viewModel ||
            TryGetPlatformHandle() is not { } handle)
        {
            return;
        }
        viewModel.ResolveDisplayProfile(handle.Handle);
    }
}
