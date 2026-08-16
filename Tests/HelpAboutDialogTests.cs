using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;

namespace HappyPhoton.Tests;

public sealed class HelpAboutDialogTests
{
    [AvaloniaFact]
    public void Dialog_ConstructsWithShortcutsSelectedAndIdentityPopulated()
    {
        var dialog = new HelpAboutDialog();

        var tabs = dialog.FindControl<TabControl>("HelpAboutTabs")!;
        var identity = dialog.FindControl<TextBlock>("AboutIdentityText")!;
        var version = dialog.FindControl<TextBlock>("VersionText")!;
        var revision = dialog.FindControl<TextBlock>("SourceRevisionText")!;
        var date = dialog.FindControl<TextBlock>("BuildDateText")!;

        Assert.Equal(0, tabs.SelectedIndex);
        Assert.Equal("Product name", AutomationProperties.GetName(identity));
        Assert.Contains(AppBuildInfo.Identity.FriendlyVersion, version.Text);
        Assert.False(string.IsNullOrWhiteSpace(revision.Text));
        Assert.False(string.IsNullOrWhiteSpace(date.Text));

        dialog.Close();
    }

    [AvaloniaFact]
    public void Dialog_RequiredActionsHaveAccessibleNames()
    {
        var dialog = new HelpAboutDialog();

        Assert.Equal(
            "Copy version info",
            AutomationProperties.GetName(
                dialog.FindControl<Button>("CopyVersionInfoButton")!));
        Assert.Equal(
            "Version information action status",
            AutomationProperties.GetName(
                dialog.FindControl<TextBlock>("CopyFeedbackText")!));
        Assert.Equal(
            "Check for updates",
            AutomationProperties.GetName(
                dialog.FindControl<Button>("CheckForUpdatesButton")!));
        Assert.Equal(
            "Update check status",
            AutomationProperties.GetName(
                dialog.FindControl<TextBlock>("UpdateStatusText")!));
        Assert.Equal(
            "Open Happy Photon project",
            AutomationProperties.GetName(dialog.FindControl<Button>("ProjectLink")!));
        Assert.Equal(
            "Open GPL license",
            AutomationProperties.GetName(dialog.FindControl<Button>("LicenseLink")!));
        Assert.Equal(
            "Open third-party notices",
            AutomationProperties.GetName(
                dialog.FindControl<Button>("ThirdPartyNoticesLink")!));

        dialog.Close();
    }

    [AvaloniaFact]
    public async Task RawRuntimeStatus_AppearsOnlyForDegradedHealth()
    {
        using var catalog = new CatalogService(NewCatalogPath());
        var healthy = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            rawRuntimeHealth: HealthyRuntime());
        var healthyDialog = new HelpAboutDialog(healthy);

        Assert.False(healthyDialog.FindControl<TextBlock>(
            "RawRuntimeStatusText")!.IsVisible);
        Assert.DoesNotContain("RAW runtime:", healthy.RawRuntimeSupportText);

        healthyDialog.Close();
        await healthy.DisposeAsync();

        var degraded = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            rawRuntimeHealth: RejectedRuntime());
        var degradedDialog = new HelpAboutDialog(degraded);
        var status = degradedDialog.FindControl<TextBlock>("RawRuntimeStatusText")!;

        Assert.True(status.IsVisible);
        Assert.Contains("fallback decoder", status.Text);
        Assert.Contains("RAW runtime: degraded", degraded.RawRuntimeSupportText);
        Assert.Contains("component=LibRaw companion", degraded.RawRuntimeSupportText);

        degradedDialog.Close();
        await degraded.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task OpenAboutSurface_UpdatesWhenRuntimeProbeCompletes()
    {
        using var catalog = new CatalogService(NewCatalogPath());
        var vm = new MainWindowViewModel(catalog, baseLoader: null);
        var dialog = new HelpAboutDialog(vm);
        var status = dialog.FindControl<TextBlock>("RawRuntimeStatusText")!;
        var pending = dialog.FindControl<TextBlock>("RawRuntimePendingText")!;

        Assert.True(vm.IsRawRuntimeHealthPending);
        Assert.True(pending.IsVisible);
        Assert.False(status.IsVisible);

        vm.ApplyRawRuntimeHealth(RejectedRuntime());
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.IsRawRuntimeHealthPending);
        Assert.False(pending.IsVisible);
        Assert.True(status.IsVisible);
        Assert.Contains("LibRaw companion rejected", status.Text);
        Assert.Contains("capability mask=0x00000080", vm.RawRuntimeSupportText);

        dialog.Close();
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task HelpButton_RemainsEnabledBeforeWorkspaceIsReady()
    {
        using var catalog = new CatalogService(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-help-about-{Guid.NewGuid():N}"));
        var vm = new MainWindowViewModel(catalog);
        var titleBar = new HappyPhotonTitleBar
        {
            DataContext = vm
        };
        var button = titleBar.FindControl<Button>("HelpAboutButton")!;

        Assert.False(vm.IsWorkspaceInteractionEnabled);
        Assert.True(button.IsEffectivelyEnabled);
        Assert.Equal("Help & About", ToolTip.GetTip(button));
        Assert.Equal("Help & About", AutomationProperties.GetName(button));

        titleBar.DataContext = null;
        await vm.DisposeAsync();
    }

    [AvaloniaTheory]
    [InlineData("v1.0.0", "Happy Photon is up to date.")]
    [InlineData("v2.0.0", "Update available · v2.0.0")]
    [InlineData(null, "Couldn’t check for updates. Try again later.")]
    public async Task ManualCheck_ReportsEveryUserVisibleState(
        string? tag,
        string expected)
    {
        var root = NewCatalogPath();
        using var catalog = new CatalogService(root);
        await catalog.InitializeAsync();
        var service = CreateUpdateService(tag);
        var vm = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            updateCheckService: service);
        var dialog = new HelpAboutDialog(vm);

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(expected, dialog.FindControl<TextBlock>("UpdateStatusText")!.Text);

        dialog.Close();
        await vm.DisposeAsync();
        catalog.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(root, recursive: true);
    }

    [AvaloniaFact]
    public async Task AvailableUpdate_ShowsIndicatorAndOpensAboutTab()
    {
        var root = NewCatalogPath();
        using var catalog = new CatalogService(root);
        await catalog.InitializeAsync();
        var vm = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            updateCheckService: CreateUpdateService("v2.0.0"));
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        var titleBar = new HappyPhotonTitleBar { DataContext = vm };
        Dispatcher.UIThread.RunJobs();

        var indicator = titleBar.FindControl<Ellipse>("UpdateAvailableIndicator")!;
        var dialog = new HelpAboutDialog(vm);

        Assert.True(indicator.IsVisible);
        Assert.Equal(1, dialog.FindControl<TabControl>("HelpAboutTabs")!.SelectedIndex);

        dialog.Close();
        titleBar.DataContext = null;
        await vm.DisposeAsync();
        catalog.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(root, recursive: true);
    }

    [AvaloniaTheory]
    [InlineData(
        UpdateInstallChannel.MicrosoftStore,
        UpdateChannelSelector.MicrosoftStoreUri,
        "The Microsoft Store manages updates for this installation.")]
    [InlineData(
        UpdateInstallChannel.GitHubRelease,
        "https://github.com/seasalim/happy-photon/releases/tag/v2.0.0",
        "Download the update from the Happy Photon release page.")]
    public async Task UpgradeAction_LaunchesChannelDestinationAndExplainsChannel(
        UpdateInstallChannel channel,
        string expectedUri,
        string expectedCopy)
    {
        var root = NewCatalogPath();
        using var catalog = new CatalogService(root);
        await catalog.InitializeAsync();
        var vm = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            updateCheckService: CreateUpdateService("v2.0.0"),
            updateInstallChannel: channel);
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Uri? launched = null;
        var dialog = new HelpAboutDialog(vm, uri =>
        {
            launched = uri;
            return Task.FromResult(true);
        });
        var button = dialog.FindControl<Button>("UpgradeButton")!;

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(expectedUri, launched?.AbsoluteUri);
        Assert.Equal(
            expectedCopy,
            dialog.FindControl<TextBlock>("UpdateChannelText")!.Text);

        dialog.Close();
        await vm.DisposeAsync();
        catalog.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(root, recursive: true);
    }

    private static UpdateCheckService CreateUpdateService(
        string? tag) =>
        new(
            "1.0.0-beta.1+revision",
            "https://github.com/seasalim/happy-photon",
            (_, _) => tag == null
                ? throw new HttpRequestException("offline")
                : Task.FromResult(
                    $$"""{"tag_name":"{{tag}}","html_url":"https://github.com/seasalim/happy-photon/releases/tag/{{tag}}"}"""));

    private static string NewCatalogPath() => Path.Combine(
        Path.GetTempPath(), $"happy-photon-help-about-{Guid.NewGuid():N}");

    private static LibRawRuntimeHealth HealthyRuntime() =>
        LibRawRuntimeHealthEvaluator.Evaluate(new(
            LibRawOutputConfiguration.Version,
            LibRawRuntimeHealthEvaluator.SupportedLibRawVersion,
            "0.22.2-Release",
            LibRawCapabilities.Jpeg | LibRawCapabilities.Zlib));

    private static LibRawRuntimeHealth RejectedRuntime() =>
        LibRawRuntimeHealthEvaluator.Evaluate(new(
            LibRawOutputConfiguration.Version,
            LibRawRuntimeHealthEvaluator.SupportedLibRawVersion,
            "0.22.2-Release",
            LibRawCapabilities.Jpeg));
}
