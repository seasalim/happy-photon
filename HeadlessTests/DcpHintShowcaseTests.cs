using Avalonia;
using Avalonia.Controls;
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

public sealed class DcpHintShowcaseTests
{
    [AvaloniaTheory]
    [InlineData("develop-profile-empty", false)]
    [InlineData("develop-profile-empty-gray", true)]
    public async Task EmptyAdobeProfileHint(string scene, bool gray)
    {
        using var fixture = new CatalogVmFixture("dcp-hint-showcase");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(catalog, new NullBaseLoader(), _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.ImageService.DcpDiscovery.EnumerateAdobeDirectory = _ => [];
        var image = new ImageFile(fixture.Path("Canon-EOS-6D.cr2"));
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        vm.IsDevelopMode = true;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded &&
            vm.CaptureBackgroundActivitySnapshot().PreviewCount == 0);
        image.RawDecodeFailed = false;
        vm.PreviewImage = new Bitmap(GoldenTestPaths.Asset("srgb-reference.jpg"));
        vm.ApplyRawProfileState(image, true, new DcpProfileState("hint", DcpProfileErrorCode.None,
            null, null, new CameraIdentity("Canon", "EOS 6D"), null));
        await TestWaits.UntilAsync(() => !vm.RawProfilePickerState.IsLoading);
        vm.ProfileGroup.IsExpanded = true;
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        ShowcaseTestHelper.Capture(scene, scope, new PixelSize(1600, 1000),
            gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark, shown =>
            {
                var picker = shown.GetVisualDescendants().OfType<RawProfilePicker>().Single();
                Assert.True(picker.FindControl<Button>("GetAdobeProfilesLink")!.IsEffectivelyVisible);
                Assert.Equal(RawProfilePickerProjector.NoAdobeProfilesMessage,
                    picker.FindControl<TextBlock>("RawProfileStatusText")!.Text);
                ShowcaseTestHelper.SettleExpanderChevrons(shown);
                var scroll = shown.GetVisualDescendants().OfType<ScrollViewer>()
                    .Single(control => control.Name == "DevelopControlsScrollViewer");
                scroll.Offset = default;
                shown.UpdateLayout();
                var link = picker.FindControl<Button>("GetAdobeProfilesLink")!;
                var location = link.TranslatePoint(default, scroll)!.Value;
                Assert.InRange(location.Y, 0, scroll.Bounds.Height - link.Bounds.Height);
            });
    }
}
