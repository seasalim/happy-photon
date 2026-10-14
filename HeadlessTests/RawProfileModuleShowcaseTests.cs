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

// FIXES-WP12 visual review: a phone's per-lens Adobe profiles, open and closed.
public sealed class RawProfileModuleShowcaseTests
{
    [AvaloniaTheory]
    [InlineData("develop-profile-modules-open", "Google", "Pixel 8 Pro", "Ultrawide Camera", true)]
    [InlineData("develop-profile-modules-closed", "Google", "Pixel 8 Pro", "Ultrawide Camera", false)]
    [InlineData("develop-profile-modules-optics", "OnePlus", "12", "back camera 6.06mm f/1.6", false)]
    public async Task ModuleProfiles(string scene, string make, string model, string selected, bool open)
    {
        using var fixture = new CatalogVmFixture("profile-modules-showcase");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(catalog, new NullBaseLoader(), _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var root = Directory.CreateDirectory(fixture.Path("adobe")).FullName;
        vm.ImageService.DcpDiscovery.EnumerateAdobeDirectory = _ => new DirectoryInfo(root).EnumerateFileSystemInfos();
        string[] modules = make == "Google"
            ? ["Wide Camera", "Ultrawide Camera", "Telephoto Camera", "Front Camera"]
            : ["back camera 6.06mm f/1.6", "front camera f/2.4"];

        for (var index = 0; index < modules.Length; index++)
        {
            SyntheticDcpFactory.WriteTemporary(root, new SyntheticDcpOptions
            {
                Name = "Adobe Standard",
                UniqueCameraModel = $"{make} {model} {modules[index]}",
                ColorMatrix1 = [1 + index * 0.01, 0, 0, 0, 1, 0, 0, 0, 1]
            }, $"module-{index}.dcp");
        }

        var image = new ImageFile(fixture.Path($"{make}-{model}.dng"));
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        vm.IsDevelopMode = true;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded &&
            vm.CaptureBackgroundActivitySnapshot().PreviewCount == 0);
        image.RawDecodeFailed = false;
        vm.PreviewImage = new Bitmap(GoldenTestPaths.Asset("srgb-reference.jpg"));
        vm.ApplyRawProfileState(image, true, new DcpProfileState("modules", DcpProfileErrorCode.None,
            null, null, new CameraIdentity(make, model), null, new(null, 24)));
        await TestWaits.UntilAsync(() => !vm.RawProfilePickerState.IsLoading);
        var label = $"Adobe Standard · {selected}";
        await vm.SelectRawProfileAsync(vm.RawProfilePickerState.Options.Single(option => option.Label == label));
        vm.ProfileGroup.IsExpanded = true;
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        ShowcaseTestHelper.Capture(scene, scope, new PixelSize(1600, 1000), ThemeVariant.Dark, shown =>
        {
            ShowcaseTestHelper.SettleExpanderChevrons(shown);
            var scroll = shown.GetVisualDescendants().OfType<ScrollViewer>()
                .Single(control => control.Name == "DevelopControlsScrollViewer");
            scroll.Offset = default;
            var picker = shown.GetVisualDescendants().OfType<RawProfilePicker>().Single();
            var comboBox = picker.FindControl<ComboBox>("RawProfileComboBox")!;
            comboBox.IsDropDownOpen = open;
            shown.UpdateLayout();

            Assert.Equal(label, vm.RawProfilePickerState.SelectedOption?.Label);
            Assert.Equal(label, ToolTip.GetTip(comboBox));
            Assert.Equal(modules.Select(module => $"Adobe Standard · {module}").Order(), vm.RawProfilePickerState.Options
                .Where(option => option.IsProfile && option.Selection?.Source == RawProfileSource.Adobe)
                .Select(option => option.Label)
                .Order());
        });
    }
}
