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
    public async Task CaptureCard(WorkspaceMode mode, bool midgray, int width, int height)
    {
        await using var scene = new TipsTestScene(width, height);
        scene.Vm.WorkspaceMode = mode;
        Assert.True(TipsTestScene.Card(scene.Window, mode).IsEffectivelyVisible);
        scene.Capture($"tips-{mode.ToString().ToLowerInvariant()}-{(midgray ? "midgray" : "dark")}-{width}",
            midgray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark);
    }
}
