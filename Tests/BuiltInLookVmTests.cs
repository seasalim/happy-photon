using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;
using static HappyPhoton.Tests.ConstructionVmProbe;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class BuiltInLookVmTests
{
    [Theory]
    [InlineData(false, "pending lens")]
    [InlineData(true, "pending lens")]
    [InlineData(false, null)]
    [InlineData(true, null)]
    public async Task PendingLensChoiceSurvivesApplyRemoveHistoryAndReload(bool removing, string? pendingLens)
    {
        using var fixture = new CatalogVmFixture("builtin-pending-lens");
        using var catalog = await fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = fixture.CreateViewModel(catalog, new NoSourceLoader(), _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally), postSelection: _ => { },
            timeProvider: clock);
        var look = vm.PresetService.BuiltInPresets[0];
        var settings = SyncTransferParityCorpus.CreateLook();
        settings.Rotation = 90;
        settings.HorizonRotation = 1.25;
        settings.Crop = new CropRegion { Left = .1, Top = .1, Right = .9, Bottom = .9 };
        settings.Lens.ProfileOverride = "stored lens";
        settings.AppliedPresetId = removing ? look.Id : null;

        if (removing)
        {
            EditSettingsLook.Apply(look.Settings, settings);
        }

        var image = new ImageFile(fixture.Path("photo.jpg")) { EditSettings = settings };
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        await catalog.SaveEditSettingsAsync(image.CatalogId, settings);
        Set(vm, "_selectedImage", image);
        Set(vm, "_hasSelectedImage", true);
        Set(vm, "_isLoadingImage", true);
        vm.IsDevelopMode = true;
        Call(vm, "LoadSlidersFrom", settings);
        Set(vm, "_cropBeforeEdit", settings.Crop.Clone());
        Set(vm, "_horizonRotationBeforeEdit", settings.HorizonRotation);
        vm.IsCropMode = true;
        vm.CurrentCrop = new CropRegion { Left = .2, Top = .3, Right = .8, Bottom = .7 };
        vm.HorizonRotation = 3.5;
        Set(vm, "_isLoadingImage", false);
        Call(vm, "BeginDevelopHistoryLoad", image);
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded);

        vm.LensProfileOverride = pendingLens;
        var pending = vm.PendingPreviewDebounceTask;
        Assert.NotNull(pending);
        Assert.False(pending.IsCompleted);
        Assert.Equal("stored lens", image.EditSettings.Lens.ProfileOverride);
        var before = (EditSettings)Call(vm, "CaptureLiveEditState")!;
        var expected = removing ? FinishingLookHarness.Remove(before) : FinishingLookHarness.Apply(before, look.Settings);
        expected.AppliedPresetId = removing ? null : look.Id;

        await vm.ApplyPresetAsync(look.Id);

        Assert.Equal(pendingLens, vm.LensProfileOverride);
        Assert.True(vm.IsCropMode);
        Assert.Equal(3.5, vm.HorizonRotation);
        Assert.Equal(settings.Rotation, vm.Rotation);
        Assert.Equal(.2, vm.CurrentCrop!.Left);
        Assert.Equal(.3, vm.CurrentCrop.Top);
        Assert.Equal(.8, vm.CurrentCrop.Right);
        Assert.Equal(.7, vm.CurrentCrop.Bottom);
        var immediateHistory = await catalog.LoadEditHistoryAsync(image.CatalogId);
        Assert.Equal(EditSettingsJson.Serialize(expected), EditSettingsJson.Serialize(immediateHistory.Entries.Last().Settings));
        Assert.Equal(EditSettingsJson.Serialize(expected), EditSettingsJson.Serialize(image.EditSettings));

        clock.Advance(TimeSpan.FromMilliseconds(150));
        await pending.WaitAsync(TestWaits.Condition);
        var history = await catalog.LoadEditHistoryAsync(image.CatalogId);
        Assert.Equal(new[] { "Original", removing ? "Preset: None" : $"Preset: {look.Name}" },
            history.Entries.Select(entry => entry.Label));
        Assert.All(history.Entries, entry => Assert.Equal(pendingLens, entry.Settings.Lens.ProfileOverride));
        Assert.Equal(EditSettingsJson.Serialize(expected), EditSettingsJson.Serialize(image.EditSettings));

        await AssertReloadedAsync(expected);
        await vm.CancelCropCommand.ExecuteAsync(null);
        clock.Advance(TimeSpan.FromMilliseconds(60));
        await vm.PendingPreviewDebounceTask!.WaitAsync(TestWaits.Condition);
        Assert.True(vm.UndoCommand.CanExecute(null));
        await vm.UndoCommand.ExecuteAsync(null);
        await AssertReloadedAsync(before);
        await vm.RedoCommand.ExecuteAsync(null);
        await AssertReloadedAsync(expected);

        async Task AssertReloadedAsync(EditSettings expectedState)
        {
            Assert.Equal(EditSettingsJson.Serialize(expectedState), EditSettingsJson.Serialize(image.EditSettings));
            Assert.Equal(pendingLens, vm.LensProfileOverride);
            var reloaded = (await catalog.LoadImageStatesAsync([image.FilePath]))[image.FilePath].Single().EditSettings;
            Assert.Equal(EditSettingsJson.Serialize(expectedState), EditSettingsJson.Serialize(reloaded));
        }
    }

    [Fact]
    public async Task HoverApplyReplaceRemoveHistoryAndReloadUseTheSameLookSubset()
    {
        using var fixture = new CatalogVmFixture("builtin-vm");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(catalog, new NoSourceLoader(), _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally), postSelection: _ => { },
            timeProvider: new TestTimeProvider());
        var settings = SyncTransferParityCorpus.CreateLook();
        settings.AppliedPresetId = null;
        var image = new ImageFile(fixture.Path("photo.jpg")) { EditSettings = settings };
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        await catalog.SaveEditSettingsAsync(image.CatalogId, settings);
        Set(vm, "_selectedImage", image);
        Set(vm, "_hasSelectedImage", true);
        Set(vm, "_isLoadingImage", true);
        settings.AppliedPresetId = "builtin_fresh_start";
        Call(vm, "LoadSlidersFrom", settings);
        Assert.Equal(settings.AppliedPresetId, vm.ActivePresetId);
        Assert.False(vm.PresetService.AreBuiltInsLoaded);
        settings.AppliedPresetId = null;
        vm.IsDevelopMode = true;
        Call(vm, "LoadSlidersFrom", settings);
        Set(vm, "_isLoadingImage", false);
        Call(vm, "BeginDevelopHistoryLoad", image);
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded);
        var baseline = (EditSettings)Call(vm, "CaptureLiveEditState")!;
        var looks = vm.PresetService.BuiltInPresets;

        var steps = (await catalog.LoadEditHistoryAsync(image.CatalogId)).Entries.Count;

        foreach (var look in looks)
        {
            var hover = await Capture(vm, () => vm.PreviewPresetHoverAsync(look.Id));
            var expected = FinishingLookHarness.Apply(baseline, look.Settings);
            expected.AppliedPresetId = hover.AppliedPresetId;
            Assert.Equal(EditSettingsJson.Serialize(expected), EditSettingsJson.Serialize(hover));
            await vm.ApplyPresetAsync(look.Id);
            expected.AppliedPresetId = look.Id;
            Assert.Equal(EditSettingsJson.Serialize(expected), EditSettingsJson.Serialize(image.EditSettings));
            var history = await catalog.LoadEditHistoryAsync(image.CatalogId);
            // The first application also materializes the Original entry.
            steps = steps == 0 ? 2 : steps + 1;
            Assert.Equal(steps, history.Entries.Count);
            Assert.Equal($"Preset: {look.Name}", history.Entries.Last().Label);
        }

        var applied = image.EditSettings.Clone();
        var active = looks.Last();
        var copied = EditSettingsTransfer.CopyGroups(applied);
        Assert.Equal(active.Id, copied.AppliedPresetId);
        var pasted = new EditSettings();
        EditSettingsTransfer.ApplyGroups(copied, pasted);
        Assert.Equal(active.Id, pasted.AppliedPresetId);
        await vm.ApplyPresetAsync(active.Id);
        var removed = FinishingLookHarness.Remove(baseline);
        removed.AppliedPresetId = null;
        Assert.Equal(EditSettingsJson.Serialize(removed), EditSettingsJson.Serialize(image.EditSettings));
        Assert.Equal("Preset: None", (await catalog.LoadEditHistoryAsync(image.CatalogId)).Entries.Last().Label);
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(EditSettingsJson.Serialize(applied), EditSettingsJson.Serialize(image.EditSettings));
        await vm.RedoCommand.ExecuteAsync(null);
        Assert.Equal(EditSettingsJson.Serialize(removed), EditSettingsJson.Serialize(image.EditSettings));
        await vm.UndoCommand.ExecuteAsync(null);
        var reloaded = (await catalog.LoadImageStatesAsync([image.FilePath]))[image.FilePath].Single().EditSettings;
        Assert.Equal(EditSettingsJson.Serialize(applied), EditSettingsJson.Serialize(reloaded));
        Set(vm, "_isLoadingImage", true);
        Call(vm, "LoadSlidersFrom", reloaded);
        Assert.Equal(active.Id, vm.ActivePresetId);
        reloaded.AppliedPresetId = "builtin_unknown";
        Call(vm, "LoadSlidersFrom", reloaded);
        Assert.Null(vm.ActivePresetId);
        Assert.Null(reloaded.AppliedPresetId);
        applied.AppliedPresetId = null;
        Assert.Equal(EditSettingsJson.Serialize(applied), EditSettingsJson.Serialize(reloaded));
        Set(vm, "_isLoadingImage", false);
    }
}
