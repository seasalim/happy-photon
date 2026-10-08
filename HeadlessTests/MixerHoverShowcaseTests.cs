using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class MixerHoverShowcaseTests
{
    [AvaloniaTheory]
    [InlineData("mixer-hover-none", null, false)]
    [InlineData("mixer-hover-none", null, true)]
    [InlineData("mixer-hover-red", "RedMixerButton", false)]
    [InlineData("mixer-hover-red", "RedMixerButton", true)]
    [InlineData("mixer-hover-blue", "BlueMixerButton", false)]
    [InlineData("mixer-hover-blue", "BlueMixerButton", true)]
    public async Task RenderBandHover(string scene, string? buttonName, bool gray)
    {
        using var fixture = new CatalogVmFixture("mixer-shots");
        using var catalog = await fixture.CreateCatalogAsync();
        var path = GoldenTestPaths.Asset("nikon-d300-colorchecker.nef");
        GoldenTestPaths.RequireReadableFixture(path);
        await using var vm = fixture.CreateViewModel(catalog, new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()), _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var image = new ImageFile(path) { CatalogId = await catalog.GetOrCreateImageAsync(path) };
        vm.Browse.SetImages([image]);
        vm.IsDevelopMode = true;
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
        var window = new MainWindow { Width = 1200, Height = 700 };
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        var theme = gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark;
        using var themeScope = new TestUiScope(theme: theme);
        vm.AppTheme = gray ? AppTheme.MidGray : AppTheme.Dark;
        vm.SoloDevelopGroup(vm.ColorMixerGroup);
        scope.Show();
        window.UpdateLayout();
        var mixer = window.GetVisualDescendants().OfType<MixerEditGroup>().Single();
        var button = mixer.FindControl<Button>(buttonName ?? "RedMixerButton")!;
        button.BringIntoView();
        window.UpdateLayout();
        ShowcaseTestHelper.SettleExpanderChevrons(window);
        ShowcaseTestHelper.Settle(() => vm.RequiredDeviceLongEdge > 0 && vm.PreviewImage is { } preview &&
            Math.Max(preview.PixelSize.Width, preview.PixelSize.Height) >= vm.RequiredDeviceLongEdge &&
            vm.InitialPreviewActivityCount == 0 && vm.ImageService.Previews.PreviewActivityCount == 0, "Resting preview idle");
        ShowcaseTestHelper.Settle(() => !vm.IsBackgroundActivityStatusVisible, "Preview status idle");
        var settledPreview = vm.PreviewImage;

        if (buttonName == null)
        {
            Assert.Null(vm.MixerBandMask);
            ShowcaseTestHelper.Capture(scene + (gray ? "-gray" : "-dark"), scope, new PixelSize(1200, 700), theme);
            return;
        }

        Assert.True(button.IsEffectivelyEnabled);
        Assert.Same(vm, mixer.DataContext);
        Assert.True(mixer.TryFindResource("SurfaceLow", mixer.ActualThemeVariant, out var resource));
        Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(resource);
        Assert.True(vm.CanEditSelectedImage && vm.IsColorEditingEnabled && vm.IsDevelopMode);
        var entered = false;
        vm.MixerMaskRenderGateAsync = () => { entered = true; return Task.CompletedTask; };
        window.MouseMove(button.TranslatePoint(new Point(12, 12), window)!.Value);
        Assert.True(button.IsPointerOver, $"Band button at {button.TranslatePoint(default, window)} did not receive the pointer");
        await vm.PendingMixerMaskTask;
        Assert.True(entered);
        Assert.NotNull(vm.MixerBandMask);
        var overlay = window.GetVisualDescendants().OfType<Image>().Single(control => control.Name == "MixerBandOverlay" && control.IsEffectivelyVisible);
        Assert.Same(vm.MixerBandMask, overlay.Source);
        Assert.False(overlay.IsHitTestVisible);
        window.MouseMove(new Point(600, 350));
        Assert.Null(vm.MixerBandMask);
        window.MouseMove(button.TranslatePoint(new Point(12, 12), window)!.Value);
        await vm.PendingMixerMaskTask;
        Assert.NotNull(vm.MixerBandMask);
        Assert.Same(settledPreview, vm.PreviewImage);
        Assert.Equal(settledPreview!.PixelSize, vm.MixerBandMask.PixelSize);
        Assert.False(vm.IsBackgroundActivityStatusVisible);
        ShowcaseTestHelper.Capture(scene + (gray ? "-gray" : "-dark"), scope, new PixelSize(1200, 700), theme);
    }
}
