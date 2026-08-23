using HappyPhoton.Models;
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
}
