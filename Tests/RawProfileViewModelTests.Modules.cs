using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class RawProfileViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IdentityScanFindsDngModulesAndPreservesSelectionOpenedBeforeIdentity(bool persistedFront)
    {
        using var catalog = await _fx.CreateCatalogAsync("phone");
        await using var vm = CreateViewModel(catalog);
        var root = Directory.CreateDirectory(_fx.Path("profiles")).FullName;
        vm.ImageService.DcpDiscovery.EnumerateAdobeDirectory = _ => new DirectoryInfo(root).EnumerateFileSystemInfos();
        var frontPath = SyntheticDcpFactory.WriteTemporary(root, new SyntheticDcpOptions
        {
            Name = "Front",
            UniqueCameraModel = "iPhone13,3 front camera"
        }, "front.dcp");
        SyntheticDcpFactory.WriteTemporary(root, new SyntheticDcpOptions
        {
            Name = "Telephoto",
            UniqueCameraModel = "iPhone13,3 back telephoto camera",
            ColorMatrix1 = [1.01, 0, 0, 0, 1, 0, 0, 0, 1]
        }, "telephoto.dcp");
        var dng = SyntheticDcpFactory.WriteTemporary(_fx.Path(""), new SyntheticDcpOptions
        {
            IncludeColorMatrix1 = false,
            UniqueCameraModel = "iPhone13,3 back camera",
            CameraCalibration1 = [1, 2]
        }, "image.dng");
        var selection = persistedFront ? vm.ImageService.DcpDiscovery.InspectUserFile(frontPath).Selection : null;
        if (selection != null) selection.Source = RawProfileSource.Adobe;
        var image = new ImageFile(dng) { EditSettings = new EditSettings { RawProfile = selection } };
        vm.SelectedImage = image;

        if (persistedFront)
        {
            await vm.OpenRawProfilePickerCommand.ExecuteAsync(null);
        }

        vm.ApplyRawProfileState(image, true, new DcpProfileState("phone", DcpProfileErrorCode.None,
            null, null, new CameraIdentity("Apple", "iPhone 12 Pro"), selection, new(null, 117)));
        await TestWaits.UntilAsync(() => !vm.RawProfilePickerState.IsLoading);

        Assert.Equal(["Telephoto", "Front"], vm.RawProfilePickerState.Options
            .Where(option => option.IsProfile && option.Selection?.Source == RawProfileSource.Adobe)
            .Select(option => option.Label));
        Assert.True(RawProfilePickerProjector.ProfilesEqual(selection, vm.RawProfilePickerState.SelectedOption?.Selection));
        Assert.True(RawProfilePickerProjector.ProfilesEqual(selection, image.EditSettings.RawProfile));
        Assert.DoesNotContain("NONE DECLARE", vm.RawProfilePickerState.StatusMessage);
        Assert.False(vm.RawProfilePickerState.ShowGetAdobeProfilesLink);

        var preferred = vm.RawProfilePickerState.Options.First(option =>
            option.IsProfile && option.Selection?.Source == RawProfileSource.Adobe);

        await vm.SelectRawProfileAsync(preferred);

        Assert.Equal(["Telephoto", "Front"], vm.RawProfilePickerState.Options
            .Where(option => option.IsProfile && option.Selection?.Source == RawProfileSource.Adobe)
            .Select(option => option.Label));
        Assert.True(RawProfilePickerProjector.ProfilesEqual(preferred.Selection,
            vm.RawProfilePickerState.SelectedOption?.Selection));
        Assert.True(RawProfilePickerProjector.ProfilesEqual(preferred.Selection, image.EditSettings.RawProfile));
    }
}
