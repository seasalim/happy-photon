using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SyncTransferParityViewModelTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public Task G1_Locals() => ReplayDestinationAsync("locals");

    [Trait("Category", "Quarantined")]
    [AvaloniaFact]
    public Task G1_Crop() => ReplayDestinationAsync("crop");

    [AvaloniaFact]
    public Task G1_Geometry() => ReplayDestinationAsync("geometry");

    [AvaloniaFact]
    public Task G1_RawProfile() => ReplayDestinationAsync("raw-profile");

    [Trait("Category", "Quarantined")]
    [AvaloniaFact]
    public Task G1_Lens() => ReplayDestinationAsync("lens");

    [AvaloniaFact]
    public Task G1_Preset() => ReplayDestinationAsync("preset");

    [AvaloniaFact]
    public Task G1_DeletedPreset() => ReplayDestinationAsync("deleted-preset");

    [AvaloniaTheory]
    [InlineData(false, "Pasted settings")]
    [InlineData(true, "Pasted settings · replaced Locals")]
    public async Task DevelopPasteConfirmationNamesOnlyPhotoSpecificEdits(bool pasteLocals, string expected)
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        await fixture.CopyAsync(await fixture.ImageAsync("source", new()));
        var settings = SyncTransferParityCorpus.CreateLook();
        settings.Texture = 12;
        settings.Locals = [new LocalAdjustment { Id = "22222222222222222222222222222222", Exposure = -.5 }];
        var target = await fixture.ImageAsync("target", settings);
        await fixture.SelectAsync(target);
        var lookGroups = EditSettingsTransfer.Groups.Where(group => group.Kind == EditSettingsGroupKind.Look);
        Assert.All(lookGroups, group => Assert.True(group.DiffersFromDefault(target.EditSettings), group.Name));
        fixture.Vm.RestorePasteGroups(EditSettingsTransfer.Groups.ToDictionary(group => group.Name,
            group => group.Kind == EditSettingsGroupKind.Look || pasteLocals && group.Name == "Locals"));

        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);

        Assert.Equal(expected, fixture.Vm.TransientStatus);
        Assert.All(lookGroups, group => Assert.False(group.DiffersFromDefault(target.EditSettings), group.Name));
        Assert.Equal(!pasteLocals, target.EditSettings.Locals is { Count: > 0 });
    }

    private async Task ReplayDestinationAsync(string name)
    {
        var destination = Assert.Single(SyncTransferParityCorpus.Destinations(), item => item.Name == name);
        var recording = new SyncTransferParityRecording($"vm-{name}");
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var index = 0;

        foreach (var source in SyncTransferParityCorpus.Sources())
        {
            var image = await fixture.ImageAsync($"source-{index}", source.Settings);
            await fixture.CopyAsync(image);
            await PasteAsync(fixture, recording, destination, source.Name, index, browse: false);
            await PasteAsync(fixture, recording, destination, source.Name, index, browse: true);
            await PresetAsync(fixture, recording, destination, source, index);
            await RestoreAsync(fixture, recording, destination, source, index);
            index++;
        }

        recording.Verify(output);
    }

    private static async Task PasteAsync(SyncTransferParityVm fixture, SyncTransferParityRecording recording,
        SyncParityCase destination, string sourceName, int index, bool browse)
    {
        var path = browse ? "browse-paste" : "develop-paste";
        var target = await fixture.ImageAsync($"{path}-{index}", destination.Settings);
        await fixture.SelectAsync(target, develop: !browse);
        var confirmations = 0;
        fixture.Vm.ShowPasteSettingsAsync = dialog =>
        {
            Assert.Equal(1, dialog.TargetCount);
            confirmations++;

            return Task.FromResult(true);
        };
        Assert.True(fixture.Vm.PasteEditSettingsCommand.CanExecute(null));
        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.Equal(browse ? 1 : 0, confirmations);
        Assert.Equal(browse ? "Applied to 1 photo" : "Pasted settings", fixture.Vm.TransientStatus);
        await fixture.RecordAsync(recording, $"{path}/{sourceName}", target);
    }

    private static async Task PresetAsync(SyncTransferParityVm fixture, SyncTransferParityRecording recording,
        SyncParityCase destination, SyncParityCase source, int index)
    {
        var preset = await fixture.Vm.PresetService.SaveUserPresetAsync(
            "Sync source", source.Settings, SyncTransferParityVm.SourcePresetId);
        var target = await fixture.ImageAsync($"preset-{index}", destination.Settings);
        await fixture.SelectAsync(target);

        var beforeHover = fixture.RenderGeneration;
        await fixture.Vm.PreviewPresetHoverAsync(preset.Id);
        Assert.True(fixture.RenderGeneration > beforeHover, "Hover must publish a fresh render.");
        await fixture.RecordAsync(recording, $"preset-hover/{source.Name}", target, includeRender: true);

        var beforeExit = fixture.RenderGeneration;
        await fixture.Vm.RestoreFromHoverAsync();
        Assert.True(fixture.RenderGeneration > beforeExit, "Hover exit must publish a fresh render.");
        await fixture.RecordAsync(recording, $"preset-hover-exit/{source.Name}", target, includeRender: true);

        await fixture.Vm.ApplyPresetAsync(preset.Id);
        Assert.Equal(preset.Id, target.EditSettings.AppliedPresetId);
        await fixture.RecordAsync(recording, $"preset-apply/{source.Name}", target);
    }

    private static async Task RestoreAsync(SyncTransferParityVm fixture, SyncTransferParityRecording recording,
        SyncParityCase destination, SyncParityCase source, int index)
    {
        var target = await fixture.ImageAsync($"history-{index}", destination.Settings);
        await fixture.Catalog.SaveEditSettingsWithHistoryAsync(target.CatalogId, target.EditSettings,
            new CatalogEditHistoryMutation(-1,
            [
                new CatalogEditHistoryEntry(0, "Source snapshot", source.Settings),
                new CatalogEditHistoryEntry(1, "Destination snapshot", destination.Settings)
            ], 1));

        await fixture.SelectAsync(target);
        Assert.True(fixture.Vm.UndoCommand.CanExecute(null));
        await fixture.Vm.UndoCommand.ExecuteAsync(null);
        Assert.True(Assert.Single(fixture.Vm.HistoryEntries, entry => entry.Label == "Source snapshot").IsCurrent);
        AssertRestored(fixture, source.Settings, target);
        await fixture.RecordAsync(recording, $"history-source/{source.Name}", target);

        await fixture.Vm.RedoCommand.ExecuteAsync(null);
        Assert.True(Assert.Single(fixture.Vm.HistoryEntries, entry => entry.Label == "Destination snapshot").IsCurrent);
        AssertRestored(fixture, destination.Settings, target);
        await fixture.RecordAsync(recording, $"history-destination/{source.Name}", target);
    }

    private static void AssertRestored(SyncTransferParityVm fixture, EditSettings snapshot, ImageFile image)
    {
        var expected = snapshot.Clone();

        if (expected.AppliedPresetId != null && fixture.Vm.PresetService.GetById(expected.AppliedPresetId) == null)
        {
            expected.AppliedPresetId = null;
        }

        Assert.Equal(EditSettingsJson.Serialize(expected), EditSettingsJson.Serialize(image.EditSettings));
        Assert.NotSame(snapshot, image.EditSettings);
    }

    [AvaloniaFact]
    public async Task G1_CropDraft()
    {
        var destination = Assert.Single(SyncTransferParityCorpus.Destinations(), item => item.Name == "crop");
        var recording = new SyncTransferParityRecording("crop-draft");
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var index = 0;

        foreach (var source in SyncTransferParityCorpus.Sources())
        {
            var image = await fixture.ImageAsync($"source-{index}", source.Settings);
            await fixture.CopyAsync(image);
            var target = await fixture.ImageAsync($"draft-{index}", destination.Settings);
            await fixture.SelectAsync(target);
            await fixture.EnterDraftAsync();
            await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);
            await fixture.RecordAsync(recording, $"develop-paste-draft/{source.Name}", target);
            await fixture.ExitDraftAsync();
            index++;
        }

        recording.Verify(output);
    }
}
