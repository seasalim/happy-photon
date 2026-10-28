using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SyncTransferParityViewModelTests(ITestOutputHelper output)
{
    [Trait("Category", "Quarantined")]
    [AvaloniaFact]
    public Task G1_Crop() => SyncTransferParityReplay.ReplayDestinationAsync(output, "crop");

    [Trait("Category", "Quarantined")]
    [AvaloniaFact]
    public Task G1_Lens() => SyncTransferParityReplay.ReplayDestinationAsync(output, "lens");

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
