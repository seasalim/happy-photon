using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using HappyPhoton.Views;
using Xunit;
using Xunit.Sdk;

namespace HappyPhoton.Tests;

public sealed class TestTeardownBaselineTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public void OldSuccessOnlyShape_LeaksThemeAndWindowOnAssertionFailure()
    {
        var application = Application.Current!;
        var previousTheme = application.RequestedThemeVariant;
        Window? window = null;

        // Headless setup has no desktop lifetime. Global routed events track
        // windows opened during this isolated probe; pre-existing windows are
        // untouched, so this count gives the open-window delta from entry.
        var openWindows = new HashSet<Window>();
        using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>(
            (sender, _) => openWindows.Add(sender));
        using var closed = Window.WindowClosedEvent.AddClassHandler<Window>(
            (sender, _) => openWindows.Remove(sender));
        try
        {
            // test-teardown-policy: allow - probe outer try/finally closes and restores prior theme.
            application.RequestedThemeVariant = HappyPhotonThemes.MidGray;
            var entryCount = openWindows.Count;
            var exception = Record.Exception(() =>
            {
                // test-teardown-policy: allow - probe outer try/finally closes and restores prior theme.
                application.RequestedThemeVariant = ThemeVariant.Dark;
                window = new Window();
                // test-teardown-policy: allow - probe outer try/finally closes and restores prior theme.
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.True(false, "Injected old-shape assertion failure");
            });

            Assert.IsType<TrueException>(exception);
            var delta = openWindows.Count - entryCount;
            output.WriteLine($"G1: theme={application.RequestedThemeVariant}; " +
                $"entry={entryCount}; open={openWindows.Count}; delta={delta:+0;-0;0}");
            Assert.Equal(ThemeVariant.Dark, application.RequestedThemeVariant);
            Assert.Equal(1, delta);
        }
        finally
        {
            try
            {
                window?.Close();
            }
            finally
            {
                // test-teardown-policy: allow - probe outer try/finally closes and restores prior theme.
                application.RequestedThemeVariant = previousTheme;
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Scope_RestoresPriorThemeAndWindowCount(bool fail)
    {
        var application = Application.Current!;
        using var priorTheme = new TestUiScope(theme: HappyPhotonThemes.MidGray);
        var openWindows = new HashSet<Window>();
        using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>(
            (sender, _) => openWindows.Add(sender));
        using var closed = Window.WindowClosedEvent.AddClassHandler<Window>(
            (sender, _) => openWindows.Remove(sender));
        var entryCount = openWindows.Count;
        var window = new Window();
        try
        {
            var exception = Record.Exception(() =>
            {
                using var scope = new TestUiScope(window, ThemeVariant.Dark);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(ThemeVariant.Dark, application.RequestedThemeVariant);
                Assert.Equal(entryCount + 1, openWindows.Count);
                if (fail) Assert.Fail("Injected scope assertion failure");
            });

            if (fail) Assert.IsType<FailException>(exception);
            else Assert.Null(exception);
            output.WriteLine($"G1 scope (fail={fail}): " +
                $"theme={application.RequestedThemeVariant}; " +
                $"delta={openWindows.Count - entryCount:+0;-0;0}");
            Assert.Equal(HappyPhotonThemes.MidGray, application.RequestedThemeVariant);
            Assert.Equal(entryCount, openWindows.Count);
            Assert.False(window.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Scope_SetupFailureClosesWindowAndRestoresPriorTheme()
    {
        using var priorTheme = new TestUiScope(theme: HappyPhotonThemes.MidGray);
        var window = new Window();
        try
        {
            var exception = Record.Exception(() => new TestUiScope(
                window, ThemeVariant.Dark,
                afterShow: () => Assert.Fail("Injected helper setup failure")));
            Assert.IsType<FailException>(exception);
            Assert.False(window.IsVisible);
            Assert.Equal(HappyPhotonThemes.MidGray,
                Application.Current!.RequestedThemeVariant);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Scope_CloseFailureStillRestoresPriorTheme()
    {
        using var priorTheme = new TestUiScope(theme: HappyPhotonThemes.MidGray);
        var window = new Window();
        EventHandler<WindowClosingEventArgs> failClose =
            (_, _) => Assert.Fail("Injected close failure");
        var scope = new TestUiScope(window, ThemeVariant.Dark);
        window.Closing += failClose;
        try
        {
            Assert.IsType<FailException>(Record.Exception(scope.Dispose));
            Assert.Equal(HappyPhotonThemes.MidGray,
                Application.Current!.RequestedThemeVariant);
        }
        finally
        {
            window.Closing -= failClose;
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MainWindowScope_RestoresThemeAndClosesBeforeOwnedDisposal(
        bool fail, bool duringSetup)
    {
        using var priorTheme = new TestUiScope(theme: HappyPhotonThemes.MidGray);
        using var fixture = new CatalogVmFixture("main-window-teardown");
        using var catalog = await fixture.CreateCatalogAsync();
        var vm = fixture.CreateViewModel(catalog);
        // test-teardown-policy: allow - ForMainWindow owns binding; outer finally closes on probe failure.
        var window = new MainWindow();
        var closed = false;
        window.Closed += (_, _) => closed = true;
        try
        {
            var exception = Record.Exception(() =>
            {
                using var scope = TestUiScope.ForMainWindow(window, vm,
                    afterShow: () =>
                    {
                        Assert.True(window.IsVisible);
                        Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
                        if (duringSetup) Assert.Fail("Injected MainWindow setup failure");
                    });
                if (fail) Assert.Fail("Injected MainWindow body failure");
            });
            if (fail) Assert.IsType<FailException>(exception);
            else Assert.Null(exception);
            Assert.True(closed);
            Assert.False(window.IsVisible);
            Assert.Null(window.DataContext);
            Assert.Equal(HappyPhotonThemes.MidGray, Application.Current!.RequestedThemeVariant);
            output.WriteLine($"G1 MainWindow (fail={fail}, setup={duringSetup}): MidGray, closed={closed}");
        }
        finally
        {
            window.DataContext = null;
            window.Close();
            // Test owns disposal; no competing async OnClosing disposal may run.
            await vm.DisposeAsync();
        }
    }

}
