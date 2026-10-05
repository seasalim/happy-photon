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
    public async Task EmptyHintUpdatesOnBackgroundScanReopenAndInstallation(bool otherCamera)
    {
        using var catalog = await _fx.CreateCatalogAsync("hint");
        await using var vm = CreateViewModel(catalog);
        var root = Directory.CreateDirectory(_fx.Path("profiles")).FullName;
        vm.ImageService.DcpDiscovery.EnumerateAdobeDirectory = _ => new DirectoryInfo(root).EnumerateFileSystemInfos();

        if (otherCamera)
        {
            SyntheticDcpFactory.WriteTemporary(root,
                new SyntheticDcpOptions { UniqueCameraModel = "Nikon D850" }, "other.dcp");
        }

        var image = new ImageFile(_fx.Path("image.cr2"));
        vm.SelectedImage = image;
        Assert.False(vm.RawProfilePickerState.ShowGetAdobeProfilesLink);
        ApplyHintIdentity(vm, image);
        await TestWaits.UntilAsync(() => !vm.RawProfilePickerState.IsLoading);
        Assert.True(vm.RawProfilePickerState.ShowGetAdobeProfilesLink);
        var status = vm.RawProfilePickerState.StatusMessage;

        if (otherCamera)
        {
            Assert.EndsWith("NONE DECLARE CANON EOS 6D", status);
        }
        else
        {
            Assert.Equal(RawProfilePickerProjector.NoAdobeProfilesMessage, status);
        }

        await vm.OpenRawProfilePickerCommand.ExecuteAsync(null);
        Assert.Equal(status, vm.RawProfilePickerState.StatusMessage);
        Assert.True(vm.RawProfilePickerState.ShowGetAdobeProfilesLink);
        await vm.AddRawProfileFileAsync(null);
        Assert.False(vm.RawProfilePickerState.ShowGetAdobeProfilesLink);
        await vm.OpenRawProfilePickerCommand.ExecuteAsync(null);
        Assert.True(vm.RawProfilePickerState.ShowGetAdobeProfilesLink);
        Assert.Contains("NOT AVAILABLE AS A LOCAL FILE", vm.RawProfilePickerState.StatusMessage);

        SyntheticDcpFactory.WriteTemporary(root,
            new SyntheticDcpOptions { Name = "New installation", UniqueCameraModel = "Canon EOS 6D" }, "installed.dcp");
        await vm.OpenRawProfilePickerCommand.ExecuteAsync(null);
        Assert.Contains(vm.RawProfilePickerState.Options, option => option.Label == "New installation" && option.CanSelect);
        Assert.False(vm.RawProfilePickerState.ShowGetAdobeProfilesLink);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("false")]
    [InlineData("throw")]
    public async Task LinkLaunchesExactlyOnceWithoutChangingState(string outcome)
    {
        using var catalog = await _fx.CreateCatalogAsync("launch");
        await using var vm = CreateViewModel(catalog);
        var calls = new List<Uri>();
        vm.LaunchUriAsync = uri =>
        {
            calls.Add(uri);

            return outcome == "throw" ? Task.FromException<bool>(new IOException("No browser")) :
                Task.FromResult(outcome == "success");
        };
        var before = vm.RawProfilePickerState;
        await vm.GetAdobeProfilesCommand.ExecuteAsync(null);

        Assert.Equal("https://helpx.adobe.com/camera-raw/using/adobe-dng-converter.html", Assert.Single(calls).AbsoluteUri);
        Assert.Same(before, vm.RawProfilePickerState);
    }

    [Fact]
    public async Task SupersededScanCannotPublishHintForAnotherImage()
    {
        using var catalog = await _fx.CreateCatalogAsync("superseded-hint");
        await using var vm = CreateViewModel(catalog);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.ImageService.DcpDiscovery.EnumerateAdobeDirectory = _ => [];
        vm.ImageService.DcpDiscovery.DiscoveryGateAsync = () =>
        {
            entered.TrySetResult();

            return release.Task;
        };
        var first = new ImageFile(_fx.Path("first.cr2"));
        vm.SelectedImage = first;
        ApplyHintIdentity(vm, first);
        var scan = vm.OpenRawProfilePickerCommand.ExecuteAsync(null);

        try
        {
            await entered.Task.WaitAsync(TestWaits.Condition);
            Assert.False(vm.RawProfilePickerState.ShowGetAdobeProfilesLink);
            vm.SelectedImage = new ImageFile(_fx.Path("second.cr2"));
        }
        finally
        {
            release.TrySetResult();
            await scan.WaitAsync(TestWaits.Condition);
        }

        Assert.False(vm.RawProfilePickerState.IsLoading);
        Assert.False(vm.RawProfilePickerState.ShowGetAdobeProfilesLink);
        Assert.Equal(RawProfilePickerProjector.AwaitingIdentityMessage, vm.RawProfilePickerState.StatusMessage);
    }

    private static void ApplyHintIdentity(MainWindowViewModel vm, ImageFile image) =>
        vm.ApplyRawProfileState(image, true, new DcpProfileState("hint", DcpProfileErrorCode.None,
            null, null, new CameraIdentity("Canon", "EOS 6D"), null));
}
