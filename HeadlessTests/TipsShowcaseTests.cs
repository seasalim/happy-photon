using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class TipsShowcaseTests
{
    [AvaloniaTheory]
    [InlineData(WorkspaceMode.Browse, false, 1200, 700)]
    [InlineData(WorkspaceMode.Develop, false, 1200, 700)]
    [InlineData(WorkspaceMode.Export, false, 1200, 700)]
    [InlineData(WorkspaceMode.Browse, true, 1200, 700)]
    [InlineData(WorkspaceMode.Develop, true, 1200, 700)]
    [InlineData(WorkspaceMode.Export, true, 1200, 700)]
    [InlineData(WorkspaceMode.Browse, false, 800, 500)]
    [InlineData(WorkspaceMode.Develop, false, 800, 500)]
    [InlineData(WorkspaceMode.Export, false, 800, 500)]
    [InlineData(WorkspaceMode.Browse, true, 800, 500)]
    [InlineData(WorkspaceMode.Develop, true, 800, 500)]
    [InlineData(WorkspaceMode.Export, true, 800, 500)]
    public async Task CaptureCard(WorkspaceMode mode, bool midgray, int width, int height)
    {
        await using var scene = new TipsTestScene(width, height);
        scene.Vm.WorkspaceMode = mode;
        Assert.True(TipsTestScene.Card(scene.Window, mode).IsEffectivelyVisible);
        scene.Capture($"tips-{mode.ToString().ToLowerInvariant()}-{(midgray ? "midgray" : "dark")}-{width}",
            midgray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureHelpFooter(bool midgray)
    {
        await using var scene = new TipsTestScene();
        var dialog = new HelpAboutDialog(scene.Vm);

        ShowcaseTestHelper.Capture($"tips-help-{(midgray ? "midgray" : "dark")}", dialog,
            new PixelSize(680, 680), midgray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark,
            shown => Assert.True(TipsTestScene.Action(shown, "Show tips").IsEffectivelyVisible));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureSettingsGeneral(bool midgray)
    {
        await using var scene = new TipsTestScene();
        scene.Vm.RestoreAppTheme(midgray ? AppTheme.MidGray : AppTheme.Dark);
        var dialog = new SettingsDialog(scene.Vm);
        dialog.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 0;

        ShowcaseTestHelper.Capture($"tips-settings-{(midgray ? "midgray" : "dark")}", dialog,
            new PixelSize(650, 610), midgray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark);
    }
}
