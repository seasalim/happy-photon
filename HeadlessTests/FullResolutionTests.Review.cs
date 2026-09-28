using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class FullResolutionTests
{
    [AvaloniaFact]
    public async Task RefinedAndRestoredSurfacesSupportHuePickAndRangeMasks()
    {
        using var fixture = new CatalogVmFixture();
        using var catalog = await fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        var loader = new FullLoader();
        await using var vm = await Start(fixture, catalog, loader, clock);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        await vm.ToggleLocalLuminanceCommand.ExecuteAsync(null);
        vm.LocalLuminanceLower = 10;
        clock.Advance(TimeSpan.FromMilliseconds(150));
        await vm.PendingPreviewDebounceTask!;
        vm.PublishRequiredDeviceLongEdge(400);
        await TestWaits.UntilAsync(() => vm.PreviewImage?.PixelSize.Width == 400);
        await vm.FullResolutionWork;
        var refined = vm.PreviewImage;

        foreach (var edge in new[] { 400, 200 })
        {
            if (edge == 200)
            {
                vm.PublishRequiredDeviceLongEdge(edge);
                await vm.FullResolutionWork;
                clock.Advance(TimeSpan.FromMilliseconds(75));
                await TestWaits.UntilAsync(() => vm.PreviewImage?.PixelSize.Width == edge);
                Assert.Equal(0, vm.HeldFullBaseCount);
                Assert.True(loader.Released);
            }

            using var basis = vm.ImageService.Previews.AcquireLocalRangeBase(
                vm.SelectedImage!, vm.SelectedImage!.EditSettings, edge, vm.PreviewImage);
            Assert.NotNull(basis);
            Assert.NotEqual(400u, basis.Base.Pixels.Width);
            Assert.True(vm.CanPickLocalHue);
            vm.ShowLocalMask = true;
            await vm.PendingLocalMaskTask;
            Assert.NotNull(vm.LocalRangeMask);
            vm.ToggleLocalHuePickCommand.Execute(null);
            Assert.True(vm.IsLocalHuePicking);
            await vm.PickLocalHueAsync(new(.5, .5));
            Assert.False(vm.IsLocalHuePicking);
            Assert.True(vm.IsLocalHueEnabled);
            await vm.FullResolutionWork;
            await vm.PendingLocalMaskTask;
            Assert.NotNull(vm.LocalRangeMask);
        }

        // Keep the old surface reachable through release: its lookup must never hold the full base.
        GC.KeepAlive(refined);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullscreenBindsRefiningRefinedAndCloudStatusForManualAndPeek(bool peek)
    {
        using var fixture = new CatalogVmFixture();
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(catalog);
        vm.ShowWorkspaceReady(HappyPhoton.ViewModels.MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.IsDevelopMode = true;
        vm.IsFullScreenMode = true;
        using var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(new(2400, 1800), new(96, 96));
        vm.PreviewImage = bitmap;
        vm.OriginalViewPixelSize = bitmap.PixelSize;
        vm.ApplyFitZoom(.5);
        var window = new MainWindow { Width = 800, Height = 600 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var viewer = window.FindControl<ZoomPanControl>("FullScreenZoomPanControl")!;
        if (peek) viewer.BeginSynchronizedLoupePeek(new NormalizedPoint(.5, .5));
        else vm.ApplyManualZoom(1);

        // Drive the real VM notification and XAML binding; lifecycle tests cover how statuses arise.
        var status = typeof(HappyPhoton.ViewModels.MainWindowViewModel).GetProperty(nameof(vm.OneToOneStatus))!;
        foreach (var expected in new[] { "1:1 · refining", "1:1", "1:1 · preview detail" })
        {
            status.SetValue(vm, expected);
            Assert.Equal(expected, viewer.OneToOneStatus);
            Assert.True(viewer.IsOneToOneStatusVisible);
            Assert.Equal(expected, viewer.FindControl<TextBlock>("LoupeStatus")!.Text);
        }
        if (peek) Assert.True(viewer.EndSynchronizedLoupePeek());
        vm.PreviewImage = null;
    }
}