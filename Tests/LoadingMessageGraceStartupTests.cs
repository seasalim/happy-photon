using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LoadingMessageGraceStartupTests
{
    [Fact]
    public async Task StartupWaitsForFirstFrameAndRestartsAfterLeavingInitializing()
    {
        using var fixture = new CatalogVmFixture("startup-grace");
        using var catalog = await fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = fixture.CreateViewModel(catalog, timeProvider: clock);
        vm.ShowStartupFailure("retry");
        vm.ShowInitializing();
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(vm.IsStartupProgressVisible);
        Assert.Equal(0, clock.TimerCount);

        vm.MarkFirstFramePainted();
        clock.Advance(TimeSpan.FromMilliseconds(499));
        Assert.False(vm.IsStartupProgressVisible);
        Assert.True(vm.IsStartupGateVisible);
        Assert.False(vm.IsWorkspaceInteractionEnabled);
        vm.MarkFirstFramePainted();
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(vm.IsStartupProgressVisible);

        vm.ShowStartupFailure("retry");
        Assert.False(vm.IsStartupProgressVisible);
        vm.ShowInitializing();
        clock.Advance(TimeSpan.FromMilliseconds(499));
        Assert.False(vm.IsStartupProgressVisible);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(vm.IsStartupProgressVisible);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        Assert.False(vm.IsStartupProgressVisible);
        Assert.Equal(0, clock.TimerCount);
    }

    [Theory]
    [InlineData(StartupGateState.Ready, false)]
    [InlineData(StartupGateState.Welcome, false)]
    [InlineData(StartupGateState.Error, false)]
    [InlineData(StartupGateState.PointerRecovery, false)]
    [InlineData(StartupGateState.Ready, true)]
    [InlineData(StartupGateState.Welcome, true)]
    [InlineData(StartupGateState.Error, true)]
    [InlineData(StartupGateState.PointerRecovery, true)]
    public async Task LeavingInitializingCancelsProgress(StartupGateState state, bool beforeFirstFrame)
    {
        using var fixture = new CatalogVmFixture("startup-grace-cancel");
        using var catalog = await fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = fixture.CreateViewModel(catalog, timeProvider: clock);

        if (!beforeFirstFrame)
        {
            vm.MarkFirstFramePainted();
            clock.Advance(TimeSpan.FromMilliseconds(499));
        }

        switch (state)
        {
            case StartupGateState.Ready:
                vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
                break;

            case StartupGateState.Welcome:
                vm.ShowFirstRunWelcome(null);
                break;

            case StartupGateState.Error:
                vm.ShowStartupFailure("failed");
                break;

            default:
                vm.StartupGateState = state;
                break;
        }

        vm.MarkFirstFramePainted();
        Assert.Equal(0, clock.TimerCount);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(vm.IsStartupProgressVisible);
        Assert.Equal(state, vm.StartupGateState);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(499)]
    [InlineData(500)]
    public async Task DisposeCancelsProgressAndCannotRearm(int elapsed)
    {
        using var fixture = new CatalogVmFixture("startup-grace-dispose");
        using var catalog = await fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        var vm = fixture.CreateViewModel(catalog, timeProvider: clock);
        vm.MarkFirstFramePainted();
        clock.Advance(TimeSpan.FromMilliseconds(elapsed));
        await vm.DisposeAsync();
        Assert.Equal(0, clock.TimerCount);
        vm.ShowStartupFailure("late");
        vm.ShowInitializing();
        vm.MarkFirstFramePainted();
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(vm.IsStartupProgressVisible);
        Assert.Equal(0, clock.TimerCount);
    }
}
