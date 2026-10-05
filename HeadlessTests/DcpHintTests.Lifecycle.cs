using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class DcpHintTests
{
    [AvaloniaTheory]
    [InlineData("none")]
    [InlineData("found")]
    [InlineData("pending")]
    public async Task WizardAndRestartUseOneKeyWithoutBlocking(string presence)
    {
        using var files = new CatalogVmFixture("dcp-lifecycle");
        var locations = Locations(files);
        var adobe = Directory.CreateDirectory(files.Path("adobe")).FullName;

        if (presence == "found")
        {
            File.WriteAllBytes(Path.Combine(adobe, "camera.dcp"), SyntheticDcpFactory.Create(new()));
        }

        var pending = new TaskCompletionSource<DcpAdobeProfilePresence>();
        var calls = 0;
        CancellationToken probeToken = default;

        for (var launch = 0; launch < 2; launch++)
        {
            using var catalog = new CatalogService();
            var vm = DcpHintTestScene.Create(catalog);
            vm.ProbeDcpProfilesAsync = token =>
            {
                calls++;
                probeToken = token;

                return presence == "pending" ? pending.Task :
                    DcpAdobeProfileIndex.ProbeAsync([adobe], token);
            };
            var window = new MainWindow { Width = 1200, Height = 700 };
            using var scope = TestUiScope.ForMainWindow(window, vm);
            var closed = new TaskCompletionSource();
            window.Closed += (_, _) => closed.TrySetResult();

            try
            {
                await window.InitializeApplicationAsync(vm, catalog, locations,
                    new CatalogLocationMigrator(locations), files.Path("pictures"));

                if (launch == 0)
                {
                    await vm.ContinueFirstRunCommand.ExecuteAsync(null);
                    await vm.ContinueFirstRunCommand.ExecuteAsync(null);
                    await DcpHintTestScene.AuditKeyWritesAsync(catalog);
                    await vm.CompleteFirstRunFromLocationAsync(files.Path("pictures"));
                    if (vm.IsFirstRunLightroomStep) vm.SkipDetectedLightroomCommand.Execute(null);

                    Assert.True(vm.IsFirstRunAllSetStep);

                    if (presence == "pending")
                    {
                        Assert.False(pending.Task.IsCompleted);
                    }
                    else
                    {
                        await vm.DcpHintProbe.WaitAsync(TestWaits.Condition);
                    }

                    DcpHintTestScene.Render(window);
                    var note = window.GetVisualDescendants().OfType<Border>()
                        .Single(control => control.Name == "DcpHintNote");
                    Assert.Equal(presence == "none", note.IsEffectivelyVisible);
                    await vm.FinishFirstRunCommand.ExecuteAsync(null);
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(probeToken.IsCancellationRequested);
                    Assert.Equal(StartupGateState.Ready, vm.StartupGateState);
                    Assert.True(vm.IsBrowseMode);
                    Assert.True(window.FindControl<FolderTreePanel>("FolderTreePanel")!.IsKeyboardFocusWithin);
                    pending.TrySetResult(DcpAdobeProfilePresence.None);
                    await vm.DcpHintProbe.WaitAsync(TestWaits.Condition);
                    Assert.Equal(presence == "none" ? "true" : null,
                        await catalog.GetAppSettingAsync(MainWindowViewModel.DcpHintPresentedKey));
                }

                await vm.BackupNoticeLoad;
                vm.ImageService.DcpDiscovery.EnumerateAdobeDirectory = _ => [];
                await DcpHintTestScene.ScanAsync(vm, files.Path("first.cr2"));
                var expected = launch == 1 && presence != "none";
                Assert.Equal(expected ? MainWindowViewModel.DcpHintNotice : null, vm.StatusMessage);
                DcpHintTestScene.Render(window);
                Assert.Equal(launch == 1 || presence == "none" ? "true" : null,
                    await catalog.GetAppSettingAsync(MainWindowViewModel.DcpHintPresentedKey));
                Assert.Equal(launch == 1 || presence == "none" ? 1 : 0,
                    await DcpHintTestScene.KeyWritesAsync(catalog, MainWindowViewModel.DcpHintPresentedKey));
                Assert.Equal(1, calls);
            }
            finally
            {
                window.Close();
                await closed.Task.WaitAsync(TestWaits.Condition);
            }
        }
    }

    [AvaloniaFact]
    public async Task ExistingInstallAcknowledgesOnceAcrossProductionCloseAndNeverProbes()
    {
        using var files = new CatalogVmFixture("dcp-existing");
        var locations = Locations(files);
        var resolved = await locations.CreateFreshAsync();

        using (var seed = new CatalogService())
        {
            await seed.InitializeAsync(resolved);
            await new AppSettingsService(seed).SaveFirstRunVersionAsync(1);
            await DcpHintTestScene.AuditKeyWritesAsync(seed);
        }

        var calls = 0;

        for (var launch = 0; launch < 2; launch++)
        {
            using var catalog = new CatalogService();
            var vm = DcpHintTestScene.Create(catalog);
            vm.ProbeDcpProfilesAsync = _ =>
            {
                calls++;

                return Task.FromResult(DcpAdobeProfilePresence.None);
            };
            var window = new MainWindow { Width = 1200, Height = 700 };
            using var scope = TestUiScope.ForMainWindow(window, vm);
            var closed = new TaskCompletionSource();
            window.Closed += (_, _) => closed.TrySetResult();

            try
            {
                await window.InitializeApplicationAsync(vm, catalog, locations,
                    new CatalogLocationMigrator(locations), files.Path("pictures"));
                await vm.BackupNoticeLoad;
                vm.ImageService.DcpDiscovery.EnumerateAdobeDirectory = _ => [];
                await DcpHintTestScene.ScanAsync(vm, files.Path("first.cr2"));
                var text = window.GetVisualDescendants().OfType<TextBlock>()
                    .Single(control => control.Name == "StatusText");
                Assert.Equal(launch == 0 ? MainWindowViewModel.DcpHintNotice : null, text.Text);
                DcpHintTestScene.Render(window);
                Assert.Equal("true", await catalog.GetAppSettingAsync(MainWindowViewModel.DcpHintPresentedKey));
                vm.TransientStatus = "An operation";
                vm.TransientStatus = null;
                await DcpHintTestScene.ScanAsync(vm, files.Path("second.cr2"));
                Assert.Null(vm.StatusMessage);
                Assert.Equal(1, await DcpHintTestScene.KeyWritesAsync(catalog, MainWindowViewModel.DcpHintPresentedKey));
                Assert.Equal(0, calls);
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
