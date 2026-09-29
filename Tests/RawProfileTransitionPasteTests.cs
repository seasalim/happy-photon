using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class RawProfileTransitionTests
{
    [Fact]
    public async Task PastePreservesTargetProfileAndSelectedIdentity()
    {
        using var catalog = await _fx.CreateCatalogAsync("paste");
        await using var vm = CreateViewModel(catalog);
        var source = new ImageFile(_fx.Path("source.dng"))
        {
            EditSettings = new EditSettings
            {
                Exposure = 1,
                RawProfile = Selection("source.dcp", '1')
            }
        };
        var targetProfile = Selection("target.dcp", '2');
        var target = new ImageFile(_fx.Path("target.dng"))
        {
            EditSettings = new EditSettings { RawProfile = targetProfile }
        };
        vm.SelectedImage = source;
        vm.CopyEditSettingsCommand.Execute(null);
        vm.SelectedImage = target;

        await vm.PasteEditSettingsCommand.ExecuteAsync(null);

        Assert.Equal(1, target.EditSettings.Exposure);
        Assert.True(RawProfilePickerProjector.ProfilesEqual(
            targetProfile,
            target.EditSettings.RawProfile));
        Assert.True(RawProfilePickerProjector.ProfilesEqual(
            targetProfile,
            vm.RawProfilePickerState.SelectedOption?.Selection));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProfilePasteSupersedesPendingDiscoveryIncludingSelectedBatchTarget(bool batch)
    {
        using var catalog = await _fx.CreateCatalogAsync("profile-paste");
        await using var vm = CreateViewModel(catalog);
        var profile = Selection("pasted.dcp", 'a');
        var source = new ImageFile(_fx.Path("source.cr2"))
        {
            EditSettings = new() { RawProfile = profile }
        };
        var target = new ImageFile(_fx.Path("target.cr2"))
        {
            EditSettings = new() { RawProfile = Selection("previous.dcp", 'b') }
        };
        var facts = new PhotoCameraFacts(new("Canon", "EOS 6D"), false);
        vm.PasteFrameReader.RememberCamera(source, facts);
        vm.PasteFrameReader.RememberCamera(target, facts);
        vm.SelectedImage = source;
        vm.CopyEditSettingsCommand.Execute(null);
        vm.SelectedImage = target;
        var gate = DiscoveryGate(vm);
        var discovery = vm.OpenRawProfilePickerCommand.ExecuteAsync(null);
        await gate.Started.Task.WaitAsync(TestWaits.Condition);

        try
        {
            vm.IsDevelopMode = !batch;
            vm.Browse.SetImages([target]);
            vm.Browse.SelectOnly(target);
            vm.RestorePasteGroups(EditSettingsTransfer.Groups.ToDictionary(group => group.Name,
                group => group.Name == "Camera Profile"));
            vm.ShowPasteSettingsAsync = _ => Task.FromResult(true);
            await vm.PasteEditSettingsCommand.ExecuteAsync(null);
            Assert.Equal(profile.ContentHash, target.EditSettings.RawProfile?.ContentHash);
            Assert.False(vm.RawProfilePickerState.IsLoading);
            var pasted = vm.RawProfilePickerState;
            gate.Release.TrySetResult();
            await discovery.WaitAsync(TestWaits.Condition);
            Assert.Same(pasted, vm.RawProfilePickerState);
            Assert.Equal(profile.ContentHash, target.EditSettings.RawProfile?.ContentHash);
        }
        finally
        {
            gate.Release.TrySetResult();
            await discovery.WaitAsync(TestWaits.Condition);
        }
    }

}
