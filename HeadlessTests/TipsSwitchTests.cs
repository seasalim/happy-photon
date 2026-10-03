using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class TipsSwitchTests
{
    [AvaloniaFact]
    public async Task G2_SettingsTogglePreservesDismissedCards()
    {
        await using var scene = new TipsTestScene();
        TipsTestScene.Click(TipsTestScene.Action(TipsTestScene.Card(scene.Window, WorkspaceMode.Browse), "Got it"));
        scene.Vm.WorkspaceMode = WorkspaceMode.Develop;
        var develop = TipsTestScene.Card(scene.Window, WorkspaceMode.Develop);
        Assert.True(develop.IsEffectivelyVisible);
        var dialog = new SettingsDialog(scene.Vm);
        using var scope = new TestUiScope(dialog, owner: scene.Window);
        var checkbox = dialog.GetVisualDescendants().OfType<CheckBox>().SingleOrDefault(control =>
            Equals(control.Content, "Show tips the first time each workspace opens"));
        Assert.True(checkbox != null, "Show tips checkbox not found");
        Assert.True(checkbox!.IsChecked);

        checkbox.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(Capture(scene).ShowTips);
        Assert.False(develop.IsEffectivelyVisible);
        checkbox.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(Capture(scene).ShowTips);
        Assert.True(develop.IsEffectivelyVisible);
        scene.Vm.WorkspaceMode = WorkspaceMode.Browse;
        var browse = TipsTestScene.Card(scene.Window, WorkspaceMode.Browse);
        Assert.False(browse.IsEffectivelyVisible);

        scene.Vm.RestoreTipsSettings(new AppSettings { ShowTips = false, BrowseTipsSeen = true });
        Dispatcher.UIThread.RunJobs();
        Assert.False(checkbox.IsChecked);
        scene.Vm.RestoreTipsSettings(new AppSettings { BrowseTipsSeen = true });
        Dispatcher.UIThread.RunJobs();
        Assert.True(checkbox.IsChecked);
        Assert.False(browse.IsEffectivelyVisible);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task G3_HelpRearmsTipsWithoutChangingSurface(bool loupe)
    {
        await using var scene = new TipsTestScene();
        scene.Vm.RestoreTipsSettings(new AppSettings
        {
            ShowTips = false,
            BrowseTipsSeen = true,
            DevelopTipsSeen = true,
            ExportTipsSeen = true
        });
        var mode = loupe ? WorkspaceMode.Browse : WorkspaceMode.Develop;
        scene.Vm.WorkspaceMode = mode;
        scene.Vm.IsLoupeMode = loupe;
        var help = scene.Vm.RequestKeyboardShortcutsAsync!();
        var dialog = Assert.Single(scene.Window.OwnedWindows.OfType<HelpAboutDialog>());

        try
        {
            var button = dialog.GetVisualDescendants().OfType<Button>()
                .SingleOrDefault(control => Equals(control.Content, "Show tips"));
            Assert.True(button != null, "Show tips not found");
            TipsTestScene.Click(button!);
            await help.WaitAsync(TestWaits.Condition);
            Assert.False(dialog.IsVisible);
            Assert.Equal(mode, scene.Vm.WorkspaceMode);
            Assert.Equal(loupe, scene.Vm.IsLoupeMode);
            var settings = Capture(scene);
            Assert.True(settings.ShowTips);
            Assert.False(settings.BrowseTipsSeen);
            Assert.False(settings.DevelopTipsSeen);
            Assert.False(settings.ExportTipsSeen);
            Assert.Equal(!loupe, TipsTestScene.Card(scene.Window, mode).IsEffectivelyVisible);

            if (loupe)
            {
                scene.Vm.IsLoupeMode = false;
                Assert.True(TipsTestScene.Card(scene.Window, WorkspaceMode.Browse).IsEffectivelyVisible);
            }
            else
            {
                foreach (var next in new[] { WorkspaceMode.Browse, WorkspaceMode.Export })
                {
                    scene.Vm.WorkspaceMode = next;
                    Assert.True(TipsTestScene.Card(scene.Window, next).IsEffectivelyVisible);
                }
            }
        }
        finally
        {
            dialog.Close();
            await help.WaitAsync(TestWaits.Condition);
        }
    }

    private static AppSettings Capture(TipsTestScene scene)
    {
        var settings = new AppSettings();
        Assert.True(scene.Vm.CaptureTipsSettings(settings));

        return settings;
    }
}
