using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

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
}
