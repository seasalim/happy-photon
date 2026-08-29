using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WindowPlacementHeadlessTests
{
    private static readonly WindowPlacementScreen[] Screens =
    [
        new(
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1040),
            1)
    ];

    [AvaloniaFact]
    public void Restore_AppliesValidPlacementBeforeShowWithoutCatalog()
    {
        using var directory = new TemporaryDirectory();
        var window = new MainWindow();
        var saved = new WindowPlacement(1, 120, 90, 1000, 620, 1, true);

        window.RestoreWindowPlacement(
            new WindowPlacementStore(directory.Path), saved, Screens);

        Assert.False(window.IsVisible);
        Assert.Equal(WindowStartupLocation.Manual, window.WindowStartupLocation);
        Assert.Equal(new PixelPoint(120, 90), window.Position);
        Assert.Equal(1000, window.Width);
        Assert.Equal(620, window.Height);
        Assert.Equal(WindowState.Maximized, window.WindowState);
        window.Close();
    }

    [AvaloniaFact]
    public void Restore_InvalidPlacement_ExplicitlyUsesCenteredDefault()
    {
        using var directory = new TemporaryDirectory();
        var window = new MainWindow();

        window.RestoreWindowPlacement(
            new WindowPlacementStore(directory.Path), null, Screens);

        Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);
        Assert.Equal(1200, window.Width);
        Assert.Equal(700, window.Height);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Capture_PreservesNormalBoundsAcrossNonNormalStates()
    {
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(directory.Path, "catalog"));
        var vm = new MainWindowViewModel(catalog);
        var window = new MainWindow { DataContext = vm };
        var normal = new WindowPlacement(1, 140, 110, 980, 610, 1, false);
        window.RestoreWindowPlacement(
            new WindowPlacementStore(Path.Combine(directory.Path, "pointer")),
            normal,
            Screens);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.WindowState = WindowState.Maximized;
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.CaptureWindowPlacement().Maximized);
        AssertNormalBounds(normal, window.CaptureWindowPlacement());

        vm.IsFullScreenMode = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.CaptureWindowPlacement().Maximized);
        AssertNormalBounds(normal, window.CaptureWindowPlacement());

        vm.IsFullScreenMode = false;
        window.WindowState = WindowState.Normal;
        Dispatcher.UIThread.RunJobs();
        vm.IsFullScreenMode = true;
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.CaptureWindowPlacement().Maximized);
        AssertNormalBounds(normal, window.CaptureWindowPlacement());

        window.WindowState = WindowState.Minimized;
        Assert.False(window.CaptureWindowPlacement().Maximized);
        AssertNormalBounds(normal, window.CaptureWindowPlacement());

        window.DataContext = null;
        window.Close();
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Capture_DefersTrackingUntilWindowStateSettles()
    {
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(directory.Path, "catalog"));
        var vm = new MainWindowViewModel(catalog);
        var window = new MainWindow { DataContext = vm };
        var normal = new WindowPlacement(1, 140, 110, 980, 610, 1, false);
        window.RestoreWindowPlacement(
            new WindowPlacementStore(directory.Path), normal, Screens);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.Position = new PixelPoint(-8, -8);
        window.WindowState = WindowState.Maximized;
        Dispatcher.UIThread.RunJobs();

        var captured = window.CaptureWindowPlacement();
        Assert.Equal(140, captured.X);
        Assert.Equal(110, captured.Y);
        Assert.True(captured.Maximized);
        window.DataContext = null;
        window.Close();
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public void Close_CapturesPendingNormalGeometrySynchronously()
    {
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(directory.Path, "catalog"));
        var vm = new MainWindowViewModel(catalog);
        var store = new WindowPlacementStore(Path.Combine(directory.Path, "pointer"));
        var window = new MainWindow { DataContext = vm };
        window.RestoreWindowPlacement(store, null, Screens);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.Position = new PixelPoint(240, 180);
        window.Close();

        var saved = Assert.IsType<WindowPlacement>(store.Load());
        Assert.Equal(240, saved.X);
        Assert.Equal(180, saved.Y);
        Assert.Equal(1200, saved.Width);
        Assert.Equal(700, saved.Height);
        Dispatcher.UIThread.RunJobs();
    }

    private static void AssertNormalBounds(
        WindowPlacement expected,
        WindowPlacement actual)
    {
        Assert.Equal(expected.X, actual.X);
        Assert.Equal(expected.Y, actual.Y);
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
    }
}
