using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RepairViewModelTests : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("repairs-vm");

    [AvaloniaFact]
    public async Task ResetUndoRedoReloadPresetsRotationAndVersionsPreserveTheRepairContract()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = _fixture.CreateViewModel(catalog, new LocalTestLoader(),
            _ => Task.CompletedTask, new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        var image = new ImageFile(_fixture.Path("photo.jpg")) { EditSettings = new() { Repairs = RepairTestWorkload.S64() } };
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        image.EditSettings = (await catalog.LoadImageStatesAsync([image.FilePath]))[image.FilePath].Single().EditSettings;
        var expected = image.EditSettings.Repairs!.Select(r => r with { }).ToList();
        vm.Browse.SetImages([image]); vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded && vm.PreviewImage != null);
        Assert.True(vm.CanReset); // Repairs alone enable Reset.
        image.EditSettings.Locals = Enumerable.Range(1, 8).Select(i => new LocalAdjustment { Ordinal = i }).ToList();
        await vm.ResetEditsCommand.ExecuteAsync(null);
        Assert.Null(image.EditSettings.Repairs); Assert.Null(image.EditSettings.Locals);
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(expected, image.EditSettings.Repairs); Assert.Equal(8, image.EditSettings.Locals!.Count);
        await vm.RedoCommand.ExecuteAsync(null);
        Assert.Null(image.EditSettings.Repairs);
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(expected, image.EditSettings.Repairs);
        var saved = (await catalog.LoadImageStatesAsync([image.FilePath]))[image.FilePath].Single().EditSettings;
        Assert.Equal(expected, saved.Repairs); Assert.Equal(8, saved.Locals!.Count);
        await vm.PresetService.UseDirectoryAsync(_fixture.Path("presets"));
        var preset = await vm.PresetService.SaveUserPresetAsync("Look", new() { Exposure = 1 });
        await vm.ApplyPresetAsync(preset.Id);
        Assert.Equal(expected, image.EditSettings.Repairs);
        await vm.ApplyPresetAsync(preset.Id);
        Assert.Null(vm.ActivePresetId);
        Assert.Equal(expected, image.EditSettings.Repairs);
        vm.RotateRightCommand.Execute(null);
        if (vm.PendingHistoryCommitTask is { } pending) await pending;
        Assert.Equal(expected, image.EditSettings.Repairs);
        await vm.NewVersionFromCurrentCommand.ExecuteAsync(null);
        var version = Assert.IsType<ImageFile>(vm.SelectedImage);
        Assert.NotSame(image, version);
        Assert.Equal(expected, version.EditSettings.Repairs);
        Assert.NotSame(image.EditSettings.Repairs, version.EditSettings.Repairs);
        version.EditSettings.Repairs![0].Opacity = .25;
        Assert.Equal(expected, image.EditSettings.Repairs);
    }

    public void Dispose() => _fixture.Dispose();
}
