using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class EffectsViewModelTests : IDisposable
{
    private readonly CatalogVmFixture _fx = new("effects-vm");

    [Fact]
    public async Task EffectsEdits_CanonicalizeResetUndoAndRedo()
    {
        using var catalog = await _fx.CreateCatalogAsync("catalog");
        await using var vm = CreateViewModel(catalog);
        var image = new ImageFile(_fx.Path("photo.jpg"));
        vm.SelectedImage = image;

        vm.Midpoint = 73;
        vm.GrainSize = GrainSize.Coarse;
        Assert.Null(image.EditSettings.Effects);
        Assert.False(vm.CanReset);

        vm.Vignette = -36;
        vm.Grain = 28;
        await TestWaits.UntilAsync(() =>
            image.EditSettings.Effects is
            {
                Vignette: -36,
                Midpoint: 73,
                Grain: 28,
                GrainSize: GrainSize.Coarse
            });
        Assert.True(vm.CanReset);

        await vm.ResetEditsCommand.ExecuteAsync(null);
        Assert.Null(image.EditSettings.Effects);
        Assert.Equal(0, vm.Vignette);
        Assert.Equal(50, vm.Midpoint);
        Assert.Equal(0, vm.Grain);
        Assert.Equal(GrainSize.Medium, vm.GrainSize);

        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(-36, image.EditSettings.Effects!.Vignette);
        Assert.Equal(28, image.EditSettings.Effects.Grain);
        Assert.Equal(GrainSize.Coarse, vm.GrainSize);

        await vm.RedoCommand.ExecuteAsync(null);
        Assert.Null(image.EditSettings.Effects);
        Assert.False(vm.CanReset);
    }

    [Fact]
    public async Task InactiveLatentState_IsSessionOnlyAndResetsOnSelectionChange()
    {
        using var catalog = _fx.CreateCatalog("latent");
        await using var vm = CreateViewModel(catalog);
        var first = new ImageFile(_fx.Path("first.jpg"));
        var second = new ImageFile(_fx.Path("second.jpg"));
        vm.SelectedImage = first;

        vm.Midpoint = 81;
        vm.GrainSize = GrainSize.Fine;
        vm.SelectedImage = second;

        Assert.Equal(50, vm.Midpoint);
        Assert.Equal(GrainSize.Medium, vm.GrainSize);
        Assert.Null(first.EditSettings.Effects);
        Assert.Null(second.EditSettings.Effects);
    }

    [Fact]
    public async Task CopyPasteAndPreset_TransferDeepClonedEffects()
    {
        using var catalog = await _fx.CreateCatalogAsync("transfer");
        await using var vm = CreateViewModel(catalog);
        await vm.PresetService.UseDirectoryAsync(_fx.Path("presets"));
        var source = new ImageFile(_fx.Path("source.jpg"));
        var target = new ImageFile(_fx.Path("target.jpg"));
        vm.SelectedImage = source;
        vm.Vignette = 32;
        vm.Midpoint = 66;
        vm.Grain = 21;
        vm.GrainSize = GrainSize.Fine;
        await TestWaits.UntilAsync(() =>
            source.EditSettings.Effects?.GrainSize == GrainSize.Fine);

        vm.CopyEditSettingsCommand.Execute(null);
        vm.SelectedImage = target;
        await vm.PasteEditSettingsCommand.ExecuteAsync(null);

        Assert.Equal(32, target.EditSettings.Effects!.Vignette);
        Assert.Equal(21, target.EditSettings.Effects.Grain);
        Assert.NotSame(source.EditSettings.Effects, target.EditSettings.Effects);

        await vm.SaveCurrentAsPresetAsync("Effects preset");
        var preset = Assert.Single(vm.PresetService.UserPresets);
        target.EditSettings.Effects!.Grain = 1;
        Assert.Equal(21, preset.Settings.Effects!.Grain);

        vm.SelectedImage = source;
        await vm.ApplyPresetAsync(preset.Id);
        Assert.Equal(32, source.EditSettings.Effects!.Vignette);
        Assert.Equal(21, source.EditSettings.Effects.Grain);
        Assert.NotSame(preset.Settings.Effects, source.EditSettings.Effects);
    }

    private MainWindowViewModel CreateViewModel(CatalogService catalog)
    {
        var vm = _fx.CreateViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        return vm;
    }

    public void Dispose() => _fx.Dispose();
}
