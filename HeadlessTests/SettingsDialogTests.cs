using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SettingsDialogTests
{
    [AvaloniaFact]
    public async Task DialogAndTitleBarExposeSettingsEntryPoints()
    {
        var root = Path.Combine(
            Path.GetTempPath(), $"happy-photon-settings-{Guid.NewGuid():N}");
        using var catalog = new CatalogService(root);
        await catalog.InitializeAsync();
        var vm = new MainWindowViewModel(catalog);
        await vm.RestoreXmpSettingsAsync();
        var dialog = new SettingsDialog(vm);
        var titleBar = new HappyPhotonTitleBar { DataContext = vm };
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, dialog.FindControl<TabControl>("SettingsTabs")!.SelectedIndex);
        Assert.Equal(3, dialog.FindControl<ComboBox>("XmpModeChoice")!.ItemCount);
        var button = titleBar.FindControl<Button>("SettingsButton")!;
        Assert.True(vm.AreXmpSettingsReady);
        Assert.True(button.IsEnabled);
        Assert.Equal("Settings", AutomationProperties.GetName(button));
        Assert.Contains(ShortcutCatalog.Groups.SelectMany(group => group.Entries),
            entry => entry.Keys == "Ctrl+," &&
                     entry.Action.Contains("Settings", StringComparison.Ordinal));

        dialog.Close();
        titleBar.DataContext = null;
        await vm.DisposeAsync();
        catalog.Dispose();
        SqliteConnection.ClearAllPools();
        Directory.Delete(root, recursive: true);
    }
}
