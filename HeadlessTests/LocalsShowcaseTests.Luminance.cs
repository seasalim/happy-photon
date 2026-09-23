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
    [InlineData("locals-luminance-panel")]
    [InlineData("locals-luminance-mask")]
    [InlineData("locals-luminance-off")]
    public async Task RenderLuminanceScene(string scene)
    {
        using var fixture = new CatalogVmFixture("luminance-shots");
        using var catalog = await fixture.CreateCatalogAsync();
        var path = GoldenTestPaths.Asset("srgb-reference.jpg");
        Assert.Equal(0, (int)File.GetAttributes(path) & (0x1000 | 0x40000 | 0x400000));
        await using var vm = fixture.CreateViewModel(catalog, new StandardBaseLoader(), _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var image = new ImageFile(path) { EditSettings = new() { Locals = [new()
        {
            Type = "radial", Rx = .35, Ry = .25, Angle = 30, Feather = .5, Exposure = -1,
            Luminance = new() { Lower = .47, Enabled = !scene.EndsWith("off") }
        }] } };
        image.CatalogId = await catalog.GetOrCreateImageAsync(path);
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        vm.Browse.SetImages([image]); vm.IsDevelopMode = true; vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.IsLocalLuminanceExpanded = true; vm.ShowLocalMask = scene.EndsWith("mask");
        await vm.PendingLocalMaskTask;
        if (vm.ShowLocalMask) Assert.NotNull(vm.LocalRangeMask);
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        ShowcaseTestHelper.Capture(scene, scope, scene.EndsWith("off") ? new PixelSize(1200, 700) : new PixelSize(1440, 900),
            scene.EndsWith("off") ? HappyPhotonThemes.MidGray : ThemeVariant.Dark,
            shown => shown.GetVisualDescendants().OfType<Avalonia.Controls.ScrollViewer>()
                .Single(control => control.Name == "DevelopControlsScrollViewer").Offset = scene.EndsWith("off") ? new Vector(0, 120) : default);
    }
}
