using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;

namespace HappyPhoton.Tests;

/// <summary>Owns a shown window and/or the shared application's prior theme.</summary>
internal sealed class TestUiScope : IDisposable
{
    private readonly Application? _application;
    private readonly ThemeVariant? _previousTheme;
    private bool _disposed;

    public Window? Window { get; }

    public static TestUiScope ForMainWindow(
        MainWindow window, MainWindowViewModel viewModel, bool show = true,
        Action? afterShow = null) =>
        new(window, null, null, afterShow, viewModel, show);

    public TestUiScope(
        Window? window = null, ThemeVariant? theme = null,
        Window? owner = null, Action? afterShow = null)
        : this(window, theme, owner, afterShow, null, true) { }

    private TestUiScope(
        Window? window, ThemeVariant? theme, Window? owner, Action? afterShow,
        MainWindowViewModel? viewModel, bool show)
    {
        if (window is MainWindow && viewModel is null)
            throw new ArgumentException("Use ForMainWindow before binding the view model.", nameof(window));
        if (viewModel is not null && window!.DataContext is not null)
            throw new ArgumentException("The scope must own the initial binding.", nameof(window));
        Window = window;
        if (theme is not null || viewModel is not null)
        {
            _application = Application.Current!;
            _previousTheme = _application.RequestedThemeVariant;
        }

        try
        {
            if (viewModel is not null) window!.DataContext = viewModel;
            if (theme is not null)
                _application!.RequestedThemeVariant = theme;
            if (show && window is not null)
            {
                if (owner is null) window.Show();
                else window.Show(owner);
            }
            afterShow?.Invoke();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try { Window!.Show(); }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            try
            {
                if (Window is MainWindow) Window.DataContext = null;
            }
            finally
            {
                Window?.Close();
            }
        }
        finally
        {
            if (_application is not null)
                _application.RequestedThemeVariant = _previousTheme;
        }
    }
}
