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
    [InlineData("brush-armed")] [InlineData("brush-painting")] [InlineData("brush-erasing")] [InlineData("brush-mask")]
    public async Task RenderBrushScene(string scene)
    {
        using var fixture = new CatalogVmFixture("brush-shots");
        using var catalog = await fixture.CreateCatalogAsync();
        var path = GoldenTestPaths.Asset("srgb-reference.jpg");
        Assert.Equal(0, (int)File.GetAttributes(path) & (0x1000 | 0x40000 | 0x400000));
        await using var vm = fixture.CreateViewModel(catalog, new StandardBaseLoader(), _ => Task.CompletedTask,
            timeProvider: new TestTimeProvider());
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var image = new ImageFile(path) { CatalogId = await catalog.GetOrCreateImageAsync(path) };
        vm.Browse.SetImages([image]); vm.IsDevelopMode = true; vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.AddBrushCommand.Execute(null);
        if (scene != "brush-armed")
        {
            vm.BeginBrushStroke(new(.35, .6));
            for (var i = 1; i < 16; i++) vm.ExtendBrushStroke(new(.35 + i * .025, .6 - .15 * Math.Sin(i / 5d)), 1000);
            if (scene == "brush-mask")
            {
                await vm.CompleteLocalsGestureAsync();
                vm.BrushMode = "erase"; vm.BrushSize = 58;
                vm.BeginBrushStroke(new(.45, .53)); vm.ExtendBrushStroke(new(.6, .53), 1000);
                await vm.CompleteLocalsGestureAsync(); vm.BrushMode = "paint"; vm.ShowLocalMask = true;
            }
            else if (scene == "brush-erasing")
            {
                // A curved in-progress erase stroke: its outline must be one envelope, no inner join marks.
                await vm.CompleteLocalsGestureAsync();
                vm.BrushMode = "erase";
                vm.BeginBrushStroke(new(.4, .45));
                for (var i = 1; i < 16; i++) vm.ExtendBrushStroke(new(.4 + i * .015, .45 + .1 * Math.Sin(i / 3d)), 1000);
            }
            await vm.PendingLocalMaskTask;
        }
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        ShowcaseTestHelper.Capture(scene, scope, new PixelSize(1200, 700), ThemeVariant.Dark, shown =>
        {
            shown.GetVisualDescendants().OfType<Avalonia.Controls.ScrollViewer>()
                .Single(control => control.Name == "DevelopControlsScrollViewer").Offset = default;
            var overlay = shown.GetVisualDescendants().OfType<LocalsOverlayControl>().Single();
            overlay.UpdateBrushHover(new(overlay.Bounds.Width * .7, overlay.Bounds.Height * .58));
            Assert.True(overlay.IsVisible);
        });
    }
}
