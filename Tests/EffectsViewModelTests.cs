using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class EffectsViewModelTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-effects-vm-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public async Task EffectsEdits_CanonicalizeResetUndoAndRedo()
    {
        using var catalog = new CatalogService(Path.Combine(_root, "catalog"));
        await catalog.InitializeAsync();
        await using var vm = CreateViewModel(catalog);
        var image = new ImageFile(Path.Combine(_root, "photo.jpg"));
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
        using var catalog = new CatalogService(Path.Combine(_root, "latent"));
        await using var vm = CreateViewModel(catalog);
        var first = new ImageFile(Path.Combine(_root, "first.jpg"));
        var second = new ImageFile(Path.Combine(_root, "second.jpg"));
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
        using var catalog = new CatalogService(Path.Combine(_root, "transfer"));
        await catalog.InitializeAsync();
        await using var vm = CreateViewModel(catalog);
        await vm.PresetService.UseDirectoryAsync(Path.Combine(_root, "presets"));
        var source = new ImageFile(Path.Combine(_root, "source.jpg"));
        var target = new ImageFile(Path.Combine(_root, "target.jpg"));
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

    private static MainWindowViewModel CreateViewModel(CatalogService catalog) =>
        new(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally))
        {
            IsDevelopMode = true
        };

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }
}
