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

public sealed class SpotsShowcaseTests
{
    [AvaloniaTheory]
    [InlineData("spots-empty")]
    [InlineData("spots-heal-selected")]
    [InlineData("spots-clone")]
    [InlineData("spots-hover-move")]
    [InlineData("spots-hover-resize")]
    public async Task RenderScene(string scene)
    {
        using var fixture = new CatalogVmFixture("spots-shots");
        using var catalog = await fixture.CreateCatalogAsync();
        var path = GoldenTestPaths.Asset("srgb-reference.jpg");
        GoldenTestPaths.RequireReadableFixture(path);
        await using var vm = fixture.CreateViewModel(catalog, new StandardBaseLoader(), _ => Task.CompletedTask);
        var image = new ImageFile(path);
        if (scene != "spots-empty") image.EditSettings.Repairs =
        [
            new() { Type = scene == "spots-clone" ? "clone" : "heal", U = .4, V = .4, Su = .65, Sv = .5, Radius = .04 },
            new() { U = .2, V = .6, Su = .3, Sv = .6, Radius = .015 },
            new() { U = .7, V = .3, Su = .8, Sv = .3, Radius = .025 }
        ];
        image.CatalogId = await catalog.GetOrCreateImageAsync(path);
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.Browse.SetImages([image]);
        vm.IsDevelopMode = true;
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
        await vm.ToggleSpotsModeCommand.ExecuteAsync(null);
        vm.SelectedSpot = vm.Spots.FirstOrDefault();
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        ShowcaseTestHelper.Capture(scene, scope, new PixelSize(1200, 700), ThemeVariant.Dark, shown =>
        {
            shown.GetVisualDescendants().OfType<Avalonia.Controls.ScrollViewer>()
                .Single(control => control.Name == "DevelopControlsScrollViewer").Offset = default;
            var overlay = shown.GetVisualDescendants().OfType<SpotsOverlayControl>().Single();
            Assert.True(overlay.IsVisible);
            Assert.True(overlay.Bounds.Width > 0);
            var point = scene switch
            {
                "spots-hover-move" => new Point(.4, .4),
                "spots-hover-resize" => new Point(.44, .4),
                _ => new Point(.9, .9)
            };
            var origin = overlay.TranslatePoint(overlay.ToCanvas(point), shown)!.Value;
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Avalonia.Headless.HeadlessWindowExtensions.MouseMove(shown, origin);
            Assert.Equal(scene switch
            {
                "spots-hover-move" => "SizeAll",
                "spots-hover-resize" => "SizeWestEast",
                _ => "None"
            }, overlay.Cursor?.ToString());
        });
    }
}
