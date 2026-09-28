using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PasteReviewRegressionTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ChangedContextCancelsPaste(bool delayDialog, bool changeMode)
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var source = await fixture.ImageAsync("source", PasteGateSupport.SourceLook());
        await fixture.CopyAsync(source);
        var target = await fixture.ImageAsync("confirmed", new EditSettings());
        var other = await fixture.ImageAsync("unconfirmed", new EditSettings());
        await fixture.SelectAsync(target);
        var before = EditSettingsJson.Serialize(target.EditSettings);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Vm.ShowPasteSettingsAsync = _ =>
        {
            if (!delayDialog) return Task.FromResult(true);

            paused.TrySetResult();

            return release.Task;
        };
        fixture.Vm.PersistAppSettingsAsync = () =>
        {
            if (delayDialog) return Task.CompletedTask;

            paused.TrySetResult();

            return release.Task;
        };
        var paste = fixture.Vm.ChoosePasteSettingsCommand.ExecuteAsync(null);

        try
        {
            await paused.Task.WaitAsync(TestWaits.Condition);

            if (changeMode)
            {
                fixture.Vm.IsDevelopMode = false;
            }
            else
            {
                await fixture.SelectAsync(other);
            }
        }
        finally
        {
            release.TrySetResult(true);
            await paste.WaitAsync(TestWaits.Condition);
        }

        foreach (var image in new[] { target, other })
        {
            Assert.Equal(before, EditSettingsJson.Serialize(image.EditSettings));
            var stored = await fixture.Catalog.LoadImageStatesAsync([image.FilePath]);
            Assert.Equal(before, EditSettingsJson.Serialize(Assert.Single(stored[image.FilePath]).EditSettings));
            var history = await fixture.Catalog.LoadEditHistoryAsync(image.CatalogId);
            Assert.DoesNotContain(history.Entries, entry => entry.Label == "Paste settings");
        }
    }

    [AvaloniaFact]
    public async Task WhiteBalanceOnlyPreservesCorpusDocumentsAndUndoBytes()
    {
        await using var fixture = new SyncTransferParityVm();
        // Resolve every marker for exact Undo; frozen replay separately tests deleted-preset cleanup.
        await fixture.InitializeAsync(includeDeletedPreset: true);
        var source = await fixture.ImageAsync("source", PasteGateSupport.SourceLook(true));
        await fixture.CopyAsync(source);
        fixture.Vm.RestorePasteGroups(EditSettingsTransfer.LookGroups.ToDictionary(
            group => group.Name, group => group.Name == "White Balance"));
        var documents = SyncTransferParityCorpus.Sources().Concat(SyncTransferParityCorpus.Destinations())
            .Append(new SyncParityCase("explicit-jpeg-default", new EditSettings
            {
                Detail = new DetailSettings { CaptureSharpen = 0 }
            })).ToArray();

        var index = 0;

        foreach (var document in documents)
        {
            var target = await fixture.ImageAsync($"target-{index++}", document.Settings);
            await fixture.SelectAsync(target);
            var before = EditSettingsJson.Serialize(target.EditSettings);
            var expected = target.EditSettings.Clone();
            expected.Wb = source.EditSettings.Wb.Clone();
            expected.AppliedPresetId = null;
            await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);
            Assert.Equal(EditSettingsJson.Serialize(expected), EditSettingsJson.Serialize(target.EditSettings));
            var stored = await fixture.Catalog.LoadImageStatesAsync([target.FilePath]);
            Assert.Equal(EditSettingsJson.Serialize(expected),
                EditSettingsJson.Serialize(Assert.Single(stored[target.FilePath]).EditSettings));
            await fixture.Vm.UndoCommand.ExecuteAsync(null);
            Assert.Equal(before, EditSettingsJson.Serialize(target.EditSettings));
            stored = await fixture.Catalog.LoadImageStatesAsync([target.FilePath]);
            Assert.Equal(before, EditSettingsJson.Serialize(Assert.Single(stored[target.FilePath]).EditSettings));
        }

        output.WriteLine($"White Balance only and exact Undo: documents={index}; differences=0");
    }

    [AvaloniaFact]
    public async Task ChangedCaptureSharpenNormalizesOnlyWhenMovedToDefault()
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var source = await fixture.ImageAsync("source", PasteGateSupport.SourceLook(true));
        await fixture.CopyAsync(source);
        var target = await fixture.ImageAsync("target", new EditSettings
        {
            Detail = new DetailSettings { CaptureSharpen = 23 }
        });
        await fixture.SelectAsync(target);
        fixture.Vm.RestorePasteGroups(EditSettingsTransfer.LookGroups.ToDictionary(
            group => group.Name, group => group.Name == "White Balance"));
        fixture.Vm.CaptureSharpen = fixture.Vm.CaptureSharpenDefault;

        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);

        Assert.Null(target.EditSettings.Detail.CaptureSharpen);
    }
}
