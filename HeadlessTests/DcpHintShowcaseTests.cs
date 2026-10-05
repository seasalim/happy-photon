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
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DcpHintShowcaseTests
{
    [AvaloniaTheory]
    [InlineData("first-run-all-set-note", false)]
    [InlineData("first-run-all-set-note-gray", true)]
    public async Task AllSetNote(string scene, bool gray)
    {
        using var files = new CatalogVmFixture();
        using var catalog = await files.CreateCatalogAsync();
        await using var vm = DcpHintTestScene.Create(catalog);
        vm.ProbeDcpProfilesAsync = _ => Task.FromResult(DcpAdobeProfilePresence.None);
        vm.ShowFirstRunWelcome(files.Root);
        vm.FirstRunStep = FirstRunStep.AllSet;
        await vm.DcpHintProbe;
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        ShowcaseTestHelper.Capture(scene, scope, new PixelSize(1200, 700),
            gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark, shown =>
            {
                Assert.True(vm.IsDcpHintNoteVisible);
                Assert.True(shown.GetVisualDescendants().OfType<Border>()
                    .Single(control => control.Name == "DcpHintNote").IsEffectivelyVisible);
            });
    }

    [AvaloniaTheory]
    [InlineData("status-bar-hint-notice", false)]
    [InlineData("status-bar-hint-notice-gray", true)]
    public async Task StatusNotice(string scene, bool gray)
    {
        using var files = new CatalogVmFixture();
        using var catalog = await files.CreateCatalogAsync();
        await using var vm = DcpHintTestScene.Create(catalog);
        vm.ShowWorkspaceReady(1);
        await vm.BackupNoticeLoad;
        vm.ImageService.DcpDiscovery.EnumerateAdobeDirectory = _ => [];
        await DcpHintTestScene.StageUndoAsync(vm, files);
        await DcpHintTestScene.ScanAsync(vm, vm.SelectedImage!.FilePath);
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        ShowcaseTestHelper.Capture(scene, scope, new PixelSize(1200, 700),
            gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark, shown =>
            {
                Assert.True(vm.IsBatchUndoOffered);
                Assert.Equal(MainWindowViewModel.DcpHintNotice, vm.StatusMessage);
                Assert.True(shown.GetVisualDescendants().OfType<Button>()
                    .Single(control => control.Name == "UndoSyncButton").IsEffectivelyVisible);
            });
    }

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

    // FIXES-DEVELOP-WP10 visual review: the scope band above a detected camera profile and above the DCP hint.
    [AvaloniaTheory]
    [InlineData("detected", false)]
    [InlineData("detected", true)]
    [InlineData("hint", false)]
    [InlineData("hint", true)]
    public async Task ScopeBandProfileRows(string state, bool gray)
    {
        using var fixture = new CatalogVmFixture("scope-band-profile");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(catalog, new NullBaseLoader(), _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        vm.AppTheme = gray ? AppTheme.MidGray : AppTheme.Dark;
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var profiles = Directory.CreateDirectory(fixture.Path("profiles")).FullName;

        if (state == "detected")
        {
            SyntheticDcpFactory.WriteTemporary(profiles,
                new SyntheticDcpOptions { Name = "Camera Standard", UniqueCameraModel = "Canon EOS 6D" }, "canon.dcp");
        }

        vm.ImageService.DcpDiscovery.EnumerateAdobeDirectory = _ => new DirectoryInfo(profiles).EnumerateFileSystemInfos();
        var image = new ImageFile(fixture.Path("Canon-EOS-6D.cr2"));
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        vm.IsDevelopMode = true;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded &&
            vm.CaptureBackgroundActivitySnapshot().PreviewCount == 0);
        image.RawDecodeFailed = false;
        ShootingInfoNavigatorTests.SetExif(image);
        vm.PreviewImage = new Bitmap(GoldenTestPaths.Asset("srgb-reference.jpg"));
        vm.Histogram = ReferenceHistogram();
        vm.ApplyRawProfileState(image, true, new DcpProfileState(state, DcpProfileErrorCode.None,
            null, null, new CameraIdentity("Canon", "EOS 6D"), null));
        await TestWaits.UntilAsync(() => !vm.RawProfilePickerState.IsLoading);
        vm.ProfileGroup.IsExpanded = true;
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        ShowcaseTestHelper.Capture($"scope-band-profile-{state}-{(gray ? "gray" : "dark")}", scope, new PixelSize(1200, 700),
            gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark, shown =>
            {
                var picker = shown.GetVisualDescendants().OfType<RawProfilePicker>().Single();
                Assert.Equal(state == "hint", picker.FindControl<Button>("GetAdobeProfilesLink")!.IsEffectivelyVisible);

                if (state == "hint")
                {
                    var status = picker.FindControl<TextBlock>("RawProfileStatusText")!;
                    Assert.Equal(RawProfilePickerProjector.NoAdobeProfilesMessage, status.Text);
                    Assert.Equal(1, status.TextLayout.TextLines.Count);
                }
                Assert.Equal(state == "detected", vm.RawProfilePickerState.Options
                    .Any(option => option.Label == "Camera Standard" && option.CanSelect));
                ShowcaseTestHelper.SettleExpanderChevrons(shown);
                shown.GetVisualDescendants().OfType<ScrollViewer>()
                    .Single(control => control.Name == "DevelopControlsScrollViewer").Offset = default;
                shown.UpdateLayout();
            });
    }

    // NullBaseLoader computes no histogram; plot the preview's pixels so the scope band is populated.
    private static HistogramData ReferenceHistogram()
    {
        using var source = new MagickImage(GoldenTestPaths.Asset("srgb-reference.jpg"));
        source.Resize(400, 0);
        using var pixels = source.GetPixels();
        var data = new HistogramData();

        foreach (var pixel in pixels)
        {
            var red = pixel.GetChannel(0) >> 8;
            var green = pixel.GetChannel(1) >> 8;
            var blue = pixel.GetChannel(2) >> 8;
            data.Red[red]++;
            data.Green[green]++;
            data.Blue[blue]++;
            data.Luminance[(int)Math.Round(0.2126 * red + 0.7152 * green + 0.0722 * blue)]++;
        }

        data.Normalize();

        return data;
    }
}
