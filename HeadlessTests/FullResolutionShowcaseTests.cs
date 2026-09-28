using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class FullResolutionShowcaseTests
{
    [AvaloniaTheory]
    [InlineData("one-to-one-refined")]
    [InlineData("one-to-one-cloud")]
    [InlineData("one-to-one-refined-fullscreen")]
    [InlineData("one-to-one-cloud-fullscreen")]
    public async Task RenderScene(string scene)
    {
        using var fixture = new CatalogVmFixture("full-shots");
        using var catalog = await fixture.CreateCatalogAsync();
        var availability = new TestSourceAvailabilityService(SourceAvailability.AvailableLocally);
        var path = GoldenTestPaths.Asset("canon-eos-6d-iso-6400.cr2");
        Assert.Equal(SourceAvailability.AvailableLocally, new SourceAvailabilityService().GetAvailability(path));
        await using var vm = fixture.CreateViewModel(catalog, new RawBaseLoader(),
            _ => Task.CompletedTask, availability);
        var image = new ImageFile(path) { EditSettings = new()
            { Repairs = HealWorkloads.Repairs(HealWorkloads.S64()) } };
        image.CatalogId = await catalog.GetOrCreateImageAsync(path);
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        vm.ShowWorkspaceReady(HappyPhoton.ViewModels.MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.Browse.SetImages([image]);
        vm.IsDevelopMode = true;
        vm.IsFullScreenMode = scene.EndsWith("-fullscreen");
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
        var window = new MainWindow { Width = 1200, Height = 700 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var viewer = window.GetVisualDescendants().OfType<ZoomPanControl>()
            .Single(control => control.Name == (vm.IsFullScreenMode ? "FullScreenZoomPanControl" : "ZoomPanControl"));
        if (scene.Contains("-cloud")) availability.Availability = SourceAvailability.RequiresHydration;
        vm.ApplyManualZoom(1);
        await TestWaits.UntilAsync(() => scene.Contains("-cloud")
            ? vm.OneToOneStatus == "1:1 · preview detail"
            : vm.PreviewImage!.PixelSize.Width > 3200 && vm.OneToOneStatus == "1:1");
        await vm.FullResolutionWork;
        Assert.Equal(vm.OneToOneStatus, viewer.FindControl<TextBlock>("LoupeStatus")!.Text);
        Assert.True(viewer.IsOneToOneStatusVisible);
        ShowcaseTestHelper.Capture(scene, scope, new PixelSize(1200, 700), ThemeVariant.Dark);
    }
}
