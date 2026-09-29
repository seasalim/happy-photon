using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SyncPhotoPasteTests
{
    [AvaloniaFact]
    public async Task DevelopLocalsReplaceAndRebindSelectionAndShowMaskLikeRestore()
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var source = await fixture.ImageAsync("source", new EditSettings
        {
            Locals = [new LocalAdjustment { Id = "11111111111111111111111111111111", Exposure = .75, Whites = 15, Blacks = -12 }]
        });
        await fixture.CopyAsync(source);
        var target = await fixture.ImageAsync("target", new EditSettings
        {
            Locals = [new LocalAdjustment { Id = "22222222222222222222222222222222", Exposure = -.5 }]
        });
        await fixture.SelectAsync(target);
        await fixture.Vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        fixture.Vm.SelectedLocal = fixture.Vm.Locals[0];
        fixture.Vm.ShowLocalMask = true;
        Choose(fixture.Vm, "Locals");
        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.Equal("11111111111111111111111111111111", Assert.Single(fixture.Vm.Locals).Id);
        Assert.Same(fixture.Vm.Locals[0], fixture.Vm.SelectedLocal);
        Assert.True(fixture.Vm.ShowLocalMask);
        Assert.True(fixture.Vm.IsLocalMaskVisible);
        Assert.Equal(15, fixture.Vm.LocalWhites);
        Assert.Equal(-12, fixture.Vm.LocalBlacks);
        Assert.Equal("Pasted settings · replaced Locals", fixture.Vm.TransientStatus);
        await fixture.Vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal("22222222222222222222222222222222", fixture.Vm.SelectedLocal!.Id);
        Assert.True(fixture.Vm.IsLocalMaskVisible);
    }

    [AvaloniaTheory]
    [InlineData("Geometry")]
    [InlineData("Crop & Straighten")]
    public async Task PhotoFramePasteDiscardsDraftAndPreservesOwnQuarterTurn(string group)
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var source = await fixture.ImageAsync("source", new EditSettings
        {
            Rotation = 270, HorizonRotation = 2, Geometry = new() { Vertical = 20 }
        });
        await fixture.CopyAsync(source);
        var target = await fixture.ImageAsync("target", new EditSettings
        {
            Rotation = 90, HorizonRotation = 1, Crop = new() { Left = .1 }
        });
        await fixture.SelectAsync(target);
        await fixture.EnterDraftAsync();
        fixture.Vm.LensProfileOverride = "pending lens";
        Choose(fixture.Vm, group);
        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.False(fixture.Vm.IsCropMode);
        Assert.Equal("pending lens", fixture.Vm.LensProfileOverride);
        Assert.Null(target.EditSettings.Lens.ProfileOverride);
        Assert.Equal(90, target.EditSettings.Rotation);
        Assert.Equal(90, fixture.Vm.Rotation);
        Assert.Equal(group == "Geometry" ? 1 : 2, fixture.Vm.HorizonRotation);
        Assert.Equal(target.EditSettings.Crop?.Left, fixture.Vm.CurrentCrop?.Left);
        await fixture.Vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(1, target.EditSettings.HorizonRotation);
        Assert.Equal(.1, target.EditSettings.Crop!.Left);
    }

    [AvaloniaFact]
    public void ReplaceCountsMatchInMemoryDocumentsWithoutReadingFiles()
    {
        var targets = new[]
        {
            new EditSettings(),
            new EditSettings { Crop = new() { Left = .1 }, Locals = [new LocalAdjustment()] },
            new EditSettings { HorizonRotation = 2, Geometry = new() { Vertical = 8 } }
        };
        var dialog = new PasteSettingsViewModel("missing.dng", 3, new Dictionary<string, bool>(), targets: targets);
        Assert.Equal("replaces own on 2 of 3", Note(dialog, "Crop & Straighten"));
        Assert.Equal("replaces own on 1 of 3", Note(dialog, "Geometry"));
        Assert.Equal("replaces own on 1 of 3", Note(dialog, "Locals"));
    }

    [AvaloniaFact]
    public async Task FactsUnavailableSkipsWholeCropAndReportsWithoutHistoryStep()
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var source = await fixture.ImageAsync("source", new EditSettings { Crop = new() { Left = .2 }, HorizonRotation = 2 });
        await fixture.CopyAsync(source);
        var target = await fixture.ImageAsync("target", new EditSettings { HorizonRotation = 1 });
        await fixture.SelectAsync(target, develop: false);
        Choose(fixture.Vm, "Crop & Straighten");
        fixture.Vm.ShowPasteSettingsAsync = _ => Task.FromResult(true);
        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.Equal(1, target.EditSettings.HorizonRotation);
        Assert.Contains("facts unavailable", fixture.Vm.TransientStatus);
        Assert.Empty((await fixture.Catalog.LoadEditHistoryAsync(target.CatalogId)).Entries);
    }

    [AvaloniaFact]
    public async Task BatchValidationLeavesInvalidTargetUnchangedAndSavesValidTarget()
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var source = await fixture.ImageAsync("source", new EditSettings { Locals = [new LocalAdjustment { Exposure = 1 }] });
        await fixture.CopyAsync(source);
        var valid = await fixture.ImageAsync("valid", new EditSettings());
        var invalid = await fixture.ImageAsync("invalid", new EditSettings());
        invalid.EditSettings.Wb = new() { Mode = WbMode.Picked };
        fixture.Vm.IsDevelopMode = false;
        fixture.Vm.Browse.SetImages([valid, invalid]);
        fixture.Vm.SelectedImage = valid;
        fixture.Vm.Browse.SelectAllVisible();
        Choose(fixture.Vm, "Locals");
        fixture.Vm.ShowPasteSettingsAsync = _ => Task.FromResult(true);
        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.Single(valid.EditSettings.Locals!);
        Assert.Null(invalid.EditSettings.Locals);
        Assert.Contains("1 unchanged (invalid settings)", fixture.Vm.TransientStatus);
        Assert.Empty((await fixture.Catalog.LoadEditHistoryAsync(invalid.CatalogId)).Entries);
        Assert.Contains((await fixture.Catalog.LoadEditHistoryAsync(valid.CatalogId)).Entries, entry => entry.Label == "Paste settings");
    }

    [AvaloniaFact]
    public async Task DevelopValidationFailureReportsUnchangedUntilNextStatus()
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var source = await fixture.ImageAsync("source", new EditSettings { Locals = [new LocalAdjustment { Exposure = 1 }] });
        await fixture.CopyAsync(source);
        var target = await fixture.ImageAsync("target", new EditSettings());
        await fixture.SelectAsync(target);
        target.EditSettings.Version = EditSettings.CurrentVersion + 1;

        Choose(fixture.Vm, "Locals");
        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.Equal("Settings unchanged (invalid settings)", fixture.Vm.TransientStatus);
        Assert.Null(target.EditSettings.Locals);
        Assert.Empty((await fixture.Catalog.LoadEditHistoryAsync(target.CatalogId)).Entries);
        var statusTimer = (CancellationTokenSource)typeof(MainWindowViewModel)
            .GetField("_transientStatusCts", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(fixture.Vm)!;
        Assert.True(statusTimer.IsCancellationRequested);

        target.EditSettings.Version = EditSettings.CurrentVersion;
        fixture.Vm.CopyEditSettingsCommand.Execute(null);
        Assert.Equal("Copied settings from target.jpg", fixture.Vm.TransientStatus);
    }

    private static string Note(PasteSettingsViewModel dialog, string name) =>
        Assert.Single(dialog.Groups, group => group.Group.Name == name).Note;

    private static void Choose(MainWindowViewModel vm, string name) =>
        vm.RestorePasteGroups(EditSettingsTransfer.Groups.ToDictionary(group => group.Name, group => group.Name == name));
}
