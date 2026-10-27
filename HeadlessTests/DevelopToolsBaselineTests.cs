using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DevelopToolsBaselineTests
{
    [AvaloniaTheory]
    [InlineData("develop-tools-normal", "normal", false, 1200, 700)]
    [InlineData("develop-tools-crop", "crop", false, 1200, 700)]
    [InlineData("develop-tools-locals-linear", "locals", false, 1200, 700)]
    [InlineData("develop-tools-crop-gray", "crop", true, 800, 500)]
    public async Task RenderScene(string scene, string mode, bool gray, int width, int height)
    {
        await WithScene(mode, width, height, async (vm, scope) =>
        {
            ShowcaseTestHelper.Capture(scene, scope, new PixelSize(width, height),
                gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark, window =>
                {
                    window.GetVisualDescendants().OfType<ScrollViewer>()
                        .Single(control => control.Name == "DevelopControlsScrollViewer").Offset = default;
                    Assert.True(vm.PreviewImage is not null);
                    Assert.True(window.Bounds.Width > 0 && window.Bounds.Height > 0);
                    if (mode == "crop")
                    {
                        var overlay = window.GetVisualDescendants().OfType<CropOverlayControl>().Single();
                        Assert.True(overlay.IsEffectivelyVisible);
                        Assert.True(overlay.Bounds.Width > 0 && overlay.Bounds.Height > 0);
                    }
                    if (mode == "locals")
                    {
                        var overlay = window.GetVisualDescendants().OfType<LocalsOverlayControl>().Single();
                        Assert.True(overlay.IsEffectivelyVisible);
                        Assert.True(overlay.Bounds.Width > 0 && overlay.Bounds.Height > 0);
                        Assert.True(vm.HasSelectedLocal);
                    }
                });
            await Task.CompletedTask;
        });
    }

    internal static async Task WithScene(string mode, int width, int height,
        Func<MainWindowViewModel, TestUiScope, Task> measure, string? sourcePath = null)
    {
        using var fixture = new CatalogVmFixture("develop-tools-baseline");
        using var catalog = await fixture.CreateCatalogAsync();
        var path = sourcePath ?? GoldenTestPaths.Asset("srgb-reference.jpg");
        Assert.Equal(0, (int)File.GetAttributes(path) & (0x1000 | 0x40000 | 0x400000));
        await using var vm = fixture.CreateViewModel(catalog, new StandardBaseLoader(), _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var image = new ImageFile(path)
        {
            EditSettings = new EditSettings
            {
                Crop = new CropRegion { Left = .1, Top = .1, Right = .9, Bottom = .9 },
                Locals = [new() { Type = mode == "radial" ? "radial" : "linear", Exposure = -1, Temperature = 30, Tint = -20, Saturation = 40 }]
            }
        };
        image.CatalogId = await catalog.GetOrCreateImageAsync(path);
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        vm.Browse.SetImages([image]);
        vm.IsDevelopMode = true;
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
        if (mode == "crop") await vm.ToggleCropModeCommand.ExecuteAsync(null);
        if (mode is "locals" or "radial") await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.ShowLocalMask = false;
        vm.IsLocalGeometryExpanded = false;
        if (vm.PendingPreviewDebounceTask is { } pending)
            await pending.WaitAsync(TestWaits.Condition);
        await TestWaits.UntilAsync(() => vm.CaptureBackgroundActivitySnapshot().PreviewCount == 0
            && !vm.IsBackgroundActivityStatusVisible);
        var window = new MainWindow { Width = width, Height = height };
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        await measure(vm, scope);
    }
}
