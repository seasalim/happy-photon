using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsShowcaseTests
{
    [AvaloniaTheory]
    [InlineData("locals-hue-panel")]
    [InlineData("locals-hue-mask")]
    [InlineData("locals-hue-picking")]
    [InlineData("locals-hue-monochrome")]
    public async Task RenderHueScene(string scene)
    {
        using var fixture = new CatalogVmFixture("hue-shots");
        using var catalog = await fixture.CreateCatalogAsync();
        var path = GoldenTestPaths.Asset(scene.EndsWith("monochrome") ? "pentax-k-r.dng" : "iphone-14-pro-iso-1000.heic");
        Assert.Equal(0, (int)File.GetAttributes(path) & (0x1000 | 0x40000 | 0x400000));
        await using var vm = fixture.CreateViewModel(catalog, scene.EndsWith("monochrome") ? new LocalTestLoader(true, true, width: 1600, height: 1067) : new StandardBaseLoader(), _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var image = new ImageFile(path) { EditSettings = new() { Locals = [new()
        {
            Type = "radial", Rx = .35, Ry = .25, Angle = 30, Feather = .5, Exposure = -1,
            Luminance = new() { Lower = .3, Enabled = true },
            Hue = new() { Enabled = true, Center = 350, Width = 60, Softness = 30 }
        }] } };
        image.CatalogId = await catalog.GetOrCreateImageAsync(path);
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        vm.Browse.SetImages([image]); vm.IsDevelopMode = true; vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        if (!scene.EndsWith("monochrome"))
        {
            vm.ToggleLocalHuePickCommand.Execute(null);
            await vm.PickLocalHueAsync(scene.EndsWith("mask") ? new(.75, .35) : new(.5, .5));
            Assert.Equal("Pick Hue", vm.HistoryEntries[0].Label);
            if (!scene.EndsWith("mask")) vm.LocalHueCenter = 350;
            if (vm.PendingPreviewDebounceTask is { } pending) await pending;
        }
        vm.IsLocalHueExpanded = true; vm.IsLocalLuminanceExpanded = false; vm.ShowLocalMask = scene.EndsWith("mask");
        if (scene.EndsWith("picking")) vm.ToggleLocalHuePickCommand.Execute(null);
        await vm.PendingLocalMaskTask;
        if (vm.ShowLocalMask) Assert.NotNull(vm.LocalRangeMask);
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        ShowcaseTestHelper.Capture(scene, scope, scene.EndsWith("monochrome") ? new PixelSize(1200, 700) : new PixelSize(1440, 900),
            scene.EndsWith("monochrome") ? HappyPhotonThemes.MidGray : ThemeVariant.Dark,
            shown => shown.GetVisualDescendants().OfType<Avalonia.Controls.ScrollViewer>()
                .Single(control => control.Name == "DevelopControlsScrollViewer").Offset = scene.EndsWith("monochrome") ? new Vector(0, 190) : new Vector(0, 0));
    }
}
