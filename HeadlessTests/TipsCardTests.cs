using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class TipsCardTests
{
    [AvaloniaFact]
    public async Task G1_EachModeDismissesIndependentlyWithoutSelection()
    {
        await using var scene = new TipsTestScene();

        foreach (var mode in Enum.GetValues<WorkspaceMode>())
        {
            scene.Vm.WorkspaceMode = mode;
            var card = TipsTestScene.Card(scene.Window, mode);
            Assert.True(card.IsEffectivelyVisible);
            Assert.Null(scene.Vm.SelectedImage);
            TipsTestScene.Click(TipsTestScene.Action(card, "Got it"));
            Assert.False(card.IsEffectivelyVisible);
            scene.Vm.WorkspaceMode = mode == WorkspaceMode.Browse ? WorkspaceMode.Develop : WorkspaceMode.Browse;
            scene.Vm.WorkspaceMode = mode;
            Assert.False(card.IsEffectivelyVisible);
        }
    }

    [AvaloniaFact]
    public async Task G3_WorkspaceShortcutsRetainFocusAndApplyPick()
    {
        await using var scene = new TipsTestScene();
        var card = TipsTestScene.Card(scene.Window, WorkspaceMode.Browse);
        Assert.True(card.IsEffectivelyVisible);
        await scene.Catalog.InitializeAsync();
        var image = new ImageFile(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tips-unavailable.jpg"));
        scene.Vm.Browse.SetImages([image]);
        scene.Vm.SelectedImage = image;
        scene.Window.FindControl<BrowseGridView>("BrowseGridView")!.Focus();

        foreach (var key in new[] { Key.P, Key.D, Key.G, Key.E })
        {
            scene.Window.KeyPress(key, key == Key.E ? RawInputModifiers.Control | RawInputModifiers.Shift : RawInputModifiers.None, PhysicalKey.None, null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(card.IsKeyboardFocusWithin);

            if (key == Key.P) Assert.Equal(ImageFlag.Picked, image.Flag);
            if (key == Key.D) Assert.True(scene.Vm.IsDevelopMode);
            if (key == Key.G) Assert.True(scene.Vm.IsBrowseMode);
            if (key == Key.E) Assert.True(scene.Vm.IsExportMode);
        }
    }

    [AvaloniaFact]
    public async Task G4_OnlyReadyMainSurfacesShowCards()
    {
        await using var scene = new TipsTestScene();
        var card = TipsTestScene.Card(scene.Window, WorkspaceMode.Browse);
        Assert.True(card.IsEffectivelyVisible);
        scene.Vm.IsLoupeMode = true;
        Assert.False(card.IsEffectivelyVisible);
        scene.Vm.IsLoupeMode = false;
        Assert.True(card.IsEffectivelyVisible);
        scene.Vm.IsCompareMode = true;
        Assert.False(card.IsEffectivelyVisible);
        scene.Vm.IsCompareMode = false;

        foreach (var mode in Enum.GetValues<WorkspaceMode>())
        {
            scene.Vm.WorkspaceMode = mode;
            card = TipsTestScene.Card(scene.Window, mode);
            scene.Vm.IsFullScreenMode = true;
            Assert.False(card.IsEffectivelyVisible);
            scene.Vm.IsFullScreenMode = false;
            scene.Vm.ShowFirstRunWelcome(null);
            Assert.False(card.IsEffectivelyVisible);
            scene.Vm.ShowInitializing();
            Assert.False(card.IsEffectivelyVisible);
            scene.Vm.ShowWorkspaceReady(1);
            Assert.True(card.IsEffectivelyVisible);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task G5_ShortcutsLinkOverridesUpdateTab(bool updateAvailable)
    {
        await using var scene = new TipsTestScene();
        var card = TipsTestScene.Card(scene.Window, WorkspaceMode.Browse);
        if (updateAvailable)
        {
            scene.Vm.LatestUpdateResult = new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, new Version(99, 0, 0));
        }

        TipsTestScene.Click(TipsTestScene.Action(card, "Keyboard shortcuts"));
        var dialog = Assert.Single(scene.Window.OwnedWindows.OfType<HelpAboutDialog>());
        try
        {
            Assert.Equal(0, dialog.FindControl<TabControl>("HelpAboutTabs")!.SelectedIndex);
        }
        finally
        {
            dialog.Close();
        }
    }

    [AvaloniaFact]
    public async Task UnloadedFixturesStayInert()
    {
        await using var scene = new TipsTestScene(applySettings: false);

        foreach (var mode in Enum.GetValues<WorkspaceMode>())
        {
            scene.Vm.WorkspaceMode = mode;
            Assert.False(TipsTestScene.Card(scene.Window, mode).IsEffectivelyVisible);
        }
    }
}

