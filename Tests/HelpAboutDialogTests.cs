using Avalonia.Automation;
using Avalonia.Controls;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class HelpAboutDialogTests
{
    private readonly AvaloniaTestFixture _fixture;

    public HelpAboutDialogTests(AvaloniaTestFixture fixture)
    {
        _fixture = fixture;
    }

    [WindowsFact]
    public void Dialog_ConstructsWithShortcutsSelectedAndIdentityPopulated()
    {
        _fixture.RequireWindows();
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

    [WindowsFact]
    public void Dialog_RequiredActionsHaveAccessibleNames()
    {
        _fixture.RequireWindows();
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

    [WindowsFact]
    public async Task HelpButton_RemainsEnabledBeforeWorkspaceIsReady()
    {
        _fixture.RequireWindows();
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
