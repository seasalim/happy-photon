using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LoadingMessageGraceWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PaintedWindowStartsReplacementGraceAndReadyCancelsIt(bool readyBeforeGrace)
    {
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(directory.Path);
        var initialClock = new TestTimeProvider();
        var replacementClock = new TestTimeProvider();
        await using var initial = new MainWindowViewModel(catalog, baseLoader: null, timeProvider: initialClock);
        await using var replacement = new MainWindowViewModel(catalog, baseLoader: null, timeProvider: replacementClock);
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, initial, show: false);
        initialClock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(initial.IsStartupProgressVisible);
        Assert.Equal(0, initialClock.TimerCount);

        scope.Show();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        initialClock.Advance(TimeSpan.FromMilliseconds(499));
        Dispatcher.UIThread.RunJobs();
        Assert.False(initial.IsStartupProgressVisible);
        initialClock.Advance(TimeSpan.FromMilliseconds(1));
        Dispatcher.UIThread.RunJobs();
        Assert.True(initial.IsStartupProgressVisible);
        initial.ShowStartupFailure("restore");

        window.DataContext = replacement;
        Assert.False(replacement.IsStartupProgressVisible);
        replacementClock.Advance(TimeSpan.FromMilliseconds(499));
        Dispatcher.UIThread.RunJobs();
        Assert.False(replacement.IsStartupProgressVisible);

        if (readyBeforeGrace)
        {
            replacement.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        }

        replacementClock.Advance(TimeSpan.FromMilliseconds(1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(!readyBeforeGrace, replacement.IsStartupProgressVisible);
        replacement.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        Assert.False(replacement.IsStartupProgressVisible);
        Assert.Equal(0, replacementClock.TimerCount);
        replacementClock.Advance(TimeSpan.FromSeconds(1));
        Dispatcher.UIThread.RunJobs();
        Assert.False(replacement.IsStartupProgressVisible);
    }
}
