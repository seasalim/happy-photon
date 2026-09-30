using System.Reflection;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DevelopGroupPersistenceTests
{
    [AvaloniaFact]
    public async Task RestoreDoesNotSaveAndSoloSavesOneCompleteSnapshot()
    {
        using var fixture = new CatalogVmFixture("develop-group-restore");
        using var catalog = await fixture.CreateCatalogAsync();
        var service = new AppSettingsService(catalog);
        await service.SaveAsync(new AppSettings
        {
            RootFolderPath = "original-root",
            AppTheme = AppTheme.MidGray,
            PresetGroups = new() { ["Natural"] = false },
            DevelopGroups = new() { ["Presence"] = false, ["Unknown"] = false }
        });
        await using var vm = fixture.CreateViewModel(catalog);
        var saves = 0;
        Dictionary<string, bool>? snapshot = null;
        vm.PersistAppSettingsAsync = () =>
        {
            saves++;
            snapshot = vm.CaptureDevelopGroups();

            return Task.CompletedTask;
        };
        var loaded = await service.LoadAsync();
        vm.RestoreDevelopGroups(loaded.DevelopGroups);
        Assert.Equal(0, saves);
        Assert.False(vm.PresenceGroup.IsExpanded);
        Assert.Equal(10, vm.DevelopGroupList.Count);
        Assert.All(vm.DevelopGroupList.Where(group => group != vm.PresenceGroup),
            group => Assert.True(group.IsExpanded));
        Assert.DoesNotContain("Unknown", vm.CaptureDevelopGroups().Keys);
        var unchanged = await service.LoadAsync();
        Assert.Equal("original-root", unchanged.RootFolderPath);
        Assert.Equal(AppTheme.MidGray, unchanged.AppTheme);
        Assert.False(unchanged.PresetGroups["Natural"]);
        Assert.Equal(loaded.DevelopGroups, unchanged.DevelopGroups);

        vm.SoloDevelopGroup(vm.ColorMixerGroup);
        Assert.Equal(1, saves);
        Assert.NotNull(snapshot);
        Assert.Equal(vm.CaptureDevelopGroups(), snapshot);
        Assert.Single(snapshot, pair => pair.Value);
        vm.ColorMixerGroup.IsExpanded = false;
        Assert.Equal(2, saves);
        vm.WhiteBalanceGroup.IsExpanded = false;
        vm.IsWhiteBalancePicking = true;
        Assert.Equal(3, saves);
        Assert.True(vm.WhiteBalanceGroup.IsExpanded);
        vm.RestoreDevelopGroups(new Dictionary<string, bool>());
        Assert.Equal(3, saves);
        Assert.All(vm.DevelopGroupList, group => Assert.True(group.IsExpanded));
    }

    [AvaloniaFact]
    public async Task EverySettingsBuilderRoundTripsAcrossFolderChangeAndRestart()
    {
        using var fixture = new CatalogVmFixture("develop-group-persistence");
        using var catalog = await fixture.CreateCatalogAsync();
        var service = new AppSettingsService(catalog);
        await using var vm = fixture.CreateViewModel(catalog);
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        typeof(MainWindow).GetField("_appSettingsService", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(window, service);
        var expected = vm.DevelopGroupList.Select((group, index) => (group.Name, Expanded: index % 2 == 0))
            .ToDictionary(item => item.Name, item => item.Expanded);
        vm.RestoreDevelopGroups(expected);
        Assert.False(vm.CanPersistFolderSession);

        // The close/preferences builder must keep the groups even before startup is complete.
        await vm.PersistAppSettingsAsync!();
        Assert.Equal(expected, (await service.LoadAsync()).DevelopGroups);
        var first = fixture.Path("first");
        var second = fixture.Path("second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);

        // MainWindow.Folders has its own complete AppSettings builder.
        var complete = typeof(MainWindow).GetMethod("PersistFirstRunCompletionAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        await ((Task)complete.Invoke(window, [vm, first])!).WaitAsync(TestWaits.Condition);
        Assert.Equal(expected, (await service.LoadAsync()).DevelopGroups);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.SetRootFolder(second);
        Assert.True(vm.CanPersistFolderSession);
        await vm.PersistAppSettingsAsync!();
        var saved = await service.LoadAsync();
        Assert.Equal(second, saved.RootFolderPath);
        Assert.Equal(expected, saved.DevelopGroups);
        Assert.Equal(expected, vm.CaptureDevelopGroups());

        await using var restarted = fixture.CreateViewModel(catalog);
        restarted.RestoreDevelopGroups((await new AppSettingsService(catalog).LoadAsync()).DevelopGroups);
        Assert.Equal(expected, restarted.CaptureDevelopGroups());
    }
}
