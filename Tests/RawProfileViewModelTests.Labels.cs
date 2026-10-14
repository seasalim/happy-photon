using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class RawProfileViewModelTests
{
    [Fact]
    public async Task ModuleLabelHoldsThroughSelectionRenderRelabelReopenAndRediscovery()
    {
        using var catalog = await _fx.CreateCatalogAsync("labels");
        await using var vm = CreateViewModel(catalog);
        var root = Directory.CreateDirectory(_fx.Path("adobe")).FullName;
        vm.ImageService.DcpDiscovery.EnumerateAdobeDirectory = _ => new DirectoryInfo(root).EnumerateFileSystemInfos();
        var modules = new[] { "Wide", "Ultrawide", "Front" };

        foreach (var module in modules)
        {
            SyntheticDcpFactory.WriteTemporary(root, new SyntheticDcpOptions
            {
                Name = "Adobe Standard",
                UniqueCameraModel = $"Google Pixel 9 {module} Camera",
                ColorMatrix1 = [1 + Array.IndexOf(modules, module) * 0.01, 0, 0, 0, 1, 0, 0, 0, 1]
            }, $"Google Pixel 9 {module} Camera Adobe Standard.dcp");
        }

        var dng = SyntheticDcpFactory.WriteTemporary(_fx.Path(""), new SyntheticDcpOptions
        {
            IncludeColorMatrix1 = false,
            UniqueCameraModel = "Google Pixel 9"
        }, "PXL.dng");
        var image = new ImageFile(dng);
        var identity = new CameraIdentity("Google", "Pixel 9");
        vm.SelectedImage = image;
        vm.ApplyRawProfileState(image, true, new DcpProfileState("phone", DcpProfileErrorCode.None,
            null, null, identity, null, new(null, 24)));
        await TestWaits.UntilAsync(() => !vm.RawProfilePickerState.IsLoading);
        string[] labels =
            ["Adobe Standard · Wide Camera", "Adobe Standard · Front Camera", "Adobe Standard · Ultrawide Camera"];

        Assert.Equal(labels, AdobeLabels(vm));

        var ultrawide = vm.RawProfilePickerState.Options.Single(option => option.Label == labels[2]);
        await vm.SelectRawProfileAsync(ultrawide);

        Assert.Equal(labels[2], vm.RawProfilePickerState.SelectedOption?.Label);

        var rendered = new DcpProfileState("phone", DcpProfileErrorCode.None, null,
            DcpRenderedLabel.For(ultrawide.Selection!), identity, ultrawide.Selection, new(null, 24));
        vm.ApplyRawProfileState(image, true, rendered);

        Assert.Equal(labels[2], vm.RawProfilePickerState.SelectedOption?.Label);
        Assert.Equal(labels[2], vm.RawProfilePickerState.SelectedOption?.ClosedRowTooltip);
        Assert.Equal(labels, AdobeLabels(vm));

        await vm.OpenRawProfilePickerCommand.ExecuteAsync(null);
        await TestWaits.UntilAsync(() => !vm.RawProfilePickerState.IsLoading);

        Assert.Equal(labels[2], vm.RawProfilePickerState.SelectedOption?.Label);
        Assert.Equal(labels, AdobeLabels(vm));

        // Reopening shows the file-name anchor until the render state relabels it.
        vm.ResetRawProfilePicker(image);

        Assert.Equal("Google Pixel 9 Ultrawide Camera Adobe Standard", vm.RawProfilePickerState.SelectedOption?.Label);

        vm.ApplyRawProfileState(image, true, rendered);

        Assert.Equal(labels[2], vm.RawProfilePickerState.SelectedOption?.Label);
    }

    private static IEnumerable<string> AdobeLabels(MainWindowViewModel vm) => vm.RawProfilePickerState.Options
        .Where(option => option.IsProfile && option.Selection?.Source == RawProfileSource.Adobe)
        .Select(option => option.Label);
}
