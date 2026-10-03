using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class TipsLifecycleTests
{
    [AvaloniaFact]
    public async Task G1_TurnOffTipsPersistsThroughProductionClose()
    {
        using var files = new CatalogVmFixture("tips-off-restart");
        var locations = Locations(files);
        var resolved = await locations.CreateFreshAsync();

        using (var seed = new CatalogService())
        {
            await seed.InitializeAsync(resolved);
            await new AppSettingsService(seed).SaveFirstRunVersionAsync(1);
        }

        foreach (var firstOpen in new[] { true, false })
        {
            using var catalog = new CatalogService();
            var vm = new MainWindowViewModel(catalog);
            using var theme = new TestUiScope();
            var window = new MainWindow();
            using var scope = TestUiScope.ForMainWindow(window, vm);
            var closed = new TaskCompletionSource();
            window.Closed += (_, _) => closed.TrySetResult();

            try
            {
                await window.InitializeApplicationAsync(vm, catalog, locations,
                    new CatalogLocationMigrator(locations), files.Path("pictures"));

                if (firstOpen)
                {
                    var browse = TipsTestScene.Card(window, WorkspaceMode.Browse);
                    Assert.True(browse.IsEffectivelyVisible);
                    var button = browse.GetVisualDescendants().OfType<Button>()
                        .SingleOrDefault(control => Equals(control.Content, "Don't show tips"));
                    Assert.True(button != null, "Don't show tips not found");
                    TipsTestScene.Click(button!);
                }

                foreach (var mode in Enum.GetValues<WorkspaceMode>())
                {
                    vm.WorkspaceMode = mode;
                    Assert.False(TipsTestScene.Card(window, mode).IsEffectivelyVisible);
                }

                var settings = new AppSettings();
                Assert.True(vm.CaptureTipsSettings(settings));
                Assert.False(settings.ShowTips);
                Assert.False(settings.BrowseTipsSeen);
                Assert.False(settings.DevelopTipsSeen);
                Assert.False(settings.ExportTipsSeen);
            }
            finally
            {
                window.Close();
                await closed.Task.WaitAsync(TestWaits.Condition);
            }
        }
    }

    [AvaloniaFact]
    public async Task G2_FreshWizardFinishesInBrowseWithFolderFocus()
    {
        using var files = new CatalogVmFixture("tips-startup");
        var locations = Locations(files);
        using var catalog = new CatalogService();
        await using var vm = new MainWindowViewModel(catalog);
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm);
        await window.InitializeApplicationAsync(vm, catalog, locations, new CatalogLocationMigrator(locations), files.Path("pictures"));
        await vm.ContinueFirstRunCommand.ExecuteAsync(null);
        await vm.ContinueFirstRunCommand.ExecuteAsync(null);
        await vm.CompleteFirstRunFromLocationAsync(files.Path("pictures"));
        if (vm.IsFirstRunLightroomStep) vm.SkipDetectedLightroomCommand.Execute(null);

        var card = TipsTestScene.Card(window, WorkspaceMode.Browse);
        Assert.False(card.IsEffectivelyVisible);
        var wizard = window.GetVisualDescendants().OfType<FirstRunView>().Single();
        var finish = TipsTestScene.Action(wizard, "Start browsing");
        Assert.DoesNotContain(wizard.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible && Equals(button.Content, "Skip"));
        TipsTestScene.Click(finish);
        await Assert.IsAssignableFrom<IAsyncRelayCommand>(finish.Command).ExecutionTask!;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(StartupGateState.Ready, vm.StartupGateState);
        Assert.True(vm.IsBrowseMode);
        Assert.True(card.IsEffectivelyVisible);
        var folder = window.FindControl<FolderTreePanel>("FolderTreePanel")!;
        var tree = folder.FindControl<TreeView>("FolderTree")!;
        Assert.True(folder.IsKeyboardFocusWithin,
            $"Focus: {window.FocusManager?.GetFocusedElement()}; tree: visible={tree.IsEffectivelyVisible}, enabled={tree.IsEffectivelyEnabled}, focusable={tree.Focusable}, bounds={tree.Bounds}, active={window.IsActive}");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task G2_ExistingInstallPersistsThroughProductionClose(bool preferencesOnly)
    {
        using var files = new CatalogVmFixture("tips-restart");
        var locations = Locations(files);
        var resolved = await locations.CreateFreshAsync();
        using (var seed = new CatalogService())
        {
            await seed.InitializeAsync(resolved);
            await new AppSettingsService(seed).SaveFirstRunVersionAsync(1);
        }

        await OpenAndCloseAsync(dismiss: true);
        await OpenAndCloseAsync(dismiss: false);

        async Task OpenAndCloseAsync(bool dismiss)
        {
            using var catalog = new CatalogService();
            var vm = new MainWindowViewModel(catalog);
            using var theme = new TestUiScope();
            var window = new MainWindow();
            using var scope = TestUiScope.ForMainWindow(window, vm);
            var closed = new TaskCompletionSource();
            window.Closed += (_, _) => closed.TrySetResult();

            try
            {
                await window.InitializeApplicationAsync(vm, catalog, locations,
                    new CatalogLocationMigrator(locations), files.Path("pictures"));
                var browse = TipsTestScene.Card(window, WorkspaceMode.Browse);
                Assert.Equal(dismiss, browse.IsEffectivelyVisible);

                if (dismiss)
                {
                    TipsTestScene.Click(TipsTestScene.Action(browse, "Got it"));
                    vm.CanPersistFolderSession = !preferencesOnly;
                }
                else
                {
                    vm.WorkspaceMode = WorkspaceMode.Develop;
                    Assert.True(TipsTestScene.Card(window, WorkspaceMode.Develop).IsEffectivelyVisible);
                }
            }
            finally
            {
                window.Close();
                await closed.Task.WaitAsync(TestWaits.Condition);
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettingsFailure_CloseAndReopenPreservesTips(bool showTips)
    {
        using var files = new CatalogVmFixture("tips-settings-failure");
        var locations = Locations(files);
        var resolved = await locations.CreateFreshAsync();

        using (var seed = new CatalogService())
        {
            await seed.InitializeAsync(resolved);
            await new AppSettingsService(seed).SaveAsync(new AppSettings
            {
                FirstRunExperienceVersion = 1,
                ShowTips = showTips,
                BrowseTipsSeen = true,
                DevelopTipsSeen = true,
                ExportTipsSeen = true
            });
        }

        foreach (var failSettings in new[] { true, false })
        {
            using var catalog = new CatalogService();
            var vm = new MainWindowViewModel(catalog);
            using var theme = new TestUiScope();
            var window = new MainWindow();
            using var scope = TestUiScope.ForMainWindow(window, vm);
            var closed = new TaskCompletionSource();
            window.Closed += (_, _) => closed.TrySetResult();

            if (failSettings)
            {
                window.BeforeStartupSettingsLoad = () => throw new IOException("settings load failed");
            }

            try
            {
                await window.InitializeApplicationAsync(vm, catalog, locations,
                    new CatalogLocationMigrator(locations), files.Path("pictures"));
                Assert.Equal(failSettings, vm.IsStartupError);

                if (!failSettings)
                {
                    var saved = await new AppSettingsService(catalog).LoadAsync();
                    Assert.Equal(showTips, saved.ShowTips);
                    Assert.True(saved.BrowseTipsSeen);
                    Assert.True(saved.DevelopTipsSeen);
                    Assert.True(saved.ExportTipsSeen);

                    foreach (var mode in Enum.GetValues<WorkspaceMode>())
                    {
                        vm.WorkspaceMode = mode;
                        Assert.False(TipsTestScene.Card(window, mode).IsEffectivelyVisible);
                    }
                }
            }
            finally
            {
                window.Close();
                await closed.Task.WaitAsync(TestWaits.Condition);
            }
        }
    }

    private static AppDataLocationService Locations(CatalogVmFixture files)
    {
        Directory.CreateDirectory(files.Path("pictures"));

        return new AppDataLocationService(new AppDataPlatformPaths(
            files.Path("pictures"), files.Path("pointer"), files.Path("data"), files.Path("cache")), _ => null);
    }
}

