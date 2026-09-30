using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SyncSpotPasteTests
{
    private const string Serial = "233054000882";

    [AvaloniaFact]
    public async Task CopyWithSpotsOffThenTwoPastesReadsSerialOnceAndReplacesAll64Repairs()
    {
        var availability = new TestSourceAvailabilityService(SourceAvailability.AvailableLocally);
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync(availability: availability);
        var source = await fixture.ImageAsync("source", new() { Repairs = RepairTestWorkload.S64() });
        var target = await fixture.ImageAsync("target", new() { Repairs = [source.EditSettings.Repairs![0] with { Opacity = .25 }], Rotation = 90 });
        Remember(fixture.Vm, source);
        Remember(fixture.Vm, target);
        var reads = new List<string>();
        fixture.Vm.PasteFrameReader.SerialReader = path =>
        {
            reads.Add(path);

            return "  " + Serial + "  ";
        };
        await fixture.CopyAsync(source);
        Assert.Empty(reads);
        Remember(fixture.Vm, source, width: 8, model: "changed after Copy");
        await fixture.SelectAsync(target);
        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.Empty(reads);
        var before = target.EditSettings.Clone();
        Choose(fixture.Vm);
        await fixture.Vm.ToggleSpotsModeCommand.ExecuteAsync(null);
        fixture.Vm.SelectedSpot = fixture.Vm.Spots[0];
        Assert.True(fixture.Vm.BeginSpotsGesture(SpotHandle.Destination, new(.5, .5)));
        fixture.Vm.MoveSpotsGesture(new(.6, .7), 100);
        Assert.NotEqual(EditSettingsJson.Serialize(before), EditSettingsJson.Serialize(target.EditSettings));
        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.False(fixture.Vm.IsSpotsGestureActive);
        Assert.Equal(new[] { source.FilePath, target.FilePath }, reads);
        Assert.Equal(source.EditSettings.Repairs, target.EditSettings.Repairs);
        Assert.Equal(64, target.EditSettings.Repairs!.Count);
        Assert.NotSame(source.EditSettings.Repairs, target.EditSettings.Repairs);
        Assert.All(target.EditSettings.Repairs, repair => Assert.NotSame(source.EditSettings.Repairs![target.EditSettings.Repairs.IndexOf(repair)], repair));
        Assert.Equal(90, target.EditSettings.Rotation);
        Assert.Contains("replaced Spot Removal", fixture.Vm.TransientStatus);
        Assert.Contains(fixture.Vm.SelectedSpot!, fixture.Vm.Spots);
        availability.Resolver = path => path == source.FilePath
            ? SourceAvailability.RequiresHydration : SourceAvailability.AvailableLocally;
        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.Equal(2, reads.Count);
        Assert.Equal(1, (await fixture.Catalog.LoadEditHistoryAsync(target.CatalogId)).Position);
        await fixture.Vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(EditSettingsJson.Serialize(before), EditSettingsJson.Serialize(target.EditSettings));
    }

    [AvaloniaTheory]
    [InlineData("different camera", false)]
    [InlineData("different camera", true)]
    [InlineData("camera body unknown", true)]
    [InlineData("different camera body", true)]
    [InlineData("different sensor size", true)]
    [InlineData("camera body unknown")]
    [InlineData("zero serial")]
    [InlineData("different camera body")]
    [InlineData("different sensor size")]
    [InlineData("facts unavailable")]
    public async Task IncompatibleTargetsKeepRepairsAndGetNoHistoryStep(string reason, bool emptySource = false)
    {
        var availability = new TestSourceAvailabilityService(SourceAvailability.AvailableLocally);
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync(availability: availability);
        var source = await fixture.ImageAsync("source", new() { Repairs = RepairTestWorkload.S64() });
        if (emptySource) source.EditSettings.Repairs = null;

        var target = await fixture.ImageAsync("target", new() { Repairs = [new Repair()] });
        Remember(fixture.Vm, source);
        Remember(fixture.Vm, target, reason == "different sensor size" ? 8 : 16,
            reason == "different camera" ? "EOS R5" : "EOS 6D");
        var reads = 0;
        fixture.Vm.PasteFrameReader.SerialReader = path =>
        {
            reads++;

            return path == source.FilePath ? Serial : reason switch
            {
                "camera body unknown" => " ",
                "zero serial" => "00000",
                "different camera body" => "different-body",
                _ => Serial
            };
        };
        await fixture.CopyAsync(source);
        var before = EditSettingsJson.Serialize(target.EditSettings);
        await fixture.SelectAsync(target, develop: false);
        Choose(fixture.Vm);
        fixture.Vm.ShowPasteSettingsAsync = _ => Task.FromResult(true);

        if (reason == "facts unavailable")
        {
            availability.Resolver = path => path == source.FilePath
                ? SourceAvailability.RequiresHydration : SourceAvailability.AvailableLocally;
        }

        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.Equal(before, EditSettingsJson.Serialize(target.EditSettings));
        Assert.Empty((await fixture.Catalog.LoadEditHistoryAsync(target.CatalogId)).Entries);
        Assert.Contains(reason == "zero serial" ? "camera body unknown" : reason, fixture.Vm.TransientStatus);

        if (reason == "facts unavailable")
        {
            Assert.Equal(0, reads);
            availability.Resolver = _ => SourceAvailability.AvailableLocally;
            await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);
            Assert.Equal(2, reads);
            Assert.Equal(source.EditSettings.Repairs, target.EditSettings.Repairs);
        }
    }

    [AvaloniaFact]
    public async Task SameFileNeedsNoFactsAndResetStillClearsRepairs()
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var source = await fixture.ImageAsync("source", new() { Repairs = RepairTestWorkload.S64() });
        await fixture.CopyAsync(source);
        fixture.Vm.PasteFrameReader.Opening = (_, _) => Assert.Fail("Same file opened a header");
        await fixture.Vm.ResetEditsCommand.ExecuteAsync(null);
        Assert.Null(source.EditSettings.Repairs);
        Choose(fixture.Vm);
        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.Equal(64, source.EditSettings.Repairs!.Count);
        await fixture.Vm.ResetEditsCommand.ExecuteAsync(null);
        Assert.Null(source.EditSettings.Repairs);
    }

    [AvaloniaFact]
    public async Task EmptyCopiedRepairsClearSameBodyWithOneHistoryStep()
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var source = await fixture.ImageAsync("source", new());
        Remember(fixture.Vm, source);
        await fixture.CopyAsync(source);
        var target = await fixture.ImageAsync("target", new() { Repairs = [new Repair()] });
        await fixture.SelectAsync(target);
        Choose(fixture.Vm);
        Remember(fixture.Vm, target);
        var reads = new List<string>();
        fixture.Vm.PasteFrameReader.SerialReader = path =>
        {
            reads.Add(path);

            return Serial;
        };
        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.Null(target.EditSettings.Repairs);
        Assert.Equal(new[] { source.FilePath, target.FilePath }, reads);
        Assert.Equal(1, (await fixture.Catalog.LoadEditHistoryAsync(target.CatalogId)).Position);
    }

    [AvaloniaFact]
    public async Task EmptySourceAndTargetNeedNoFactsOrHistoryStep()
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var source = await fixture.ImageAsync("source", new());
        var target = await fixture.ImageAsync("target", new());
        await fixture.CopyAsync(source);
        await fixture.SelectAsync(target);
        Choose(fixture.Vm);
        fixture.Vm.PasteFrameReader.Opening = (_, _) => Assert.Fail("Empty transfer opened a header");

        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.Null(target.EditSettings.Repairs);
        Assert.Empty((await fixture.Catalog.LoadEditHistoryAsync(target.CatalogId)).Entries);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MonochromeRepairsTransferOnlyToSameBody(bool differentBody)
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var source = await fixture.ImageAsync("source", new() { Repairs = RepairTestWorkload.S64() });
        var target = await fixture.ImageAsync("target", new() { Repairs = [new Repair()] });
        var before = EditSettingsJson.Serialize(target.EditSettings);
        Remember(fixture.Vm, source, monochrome: true);
        Remember(fixture.Vm, target, monochrome: true);
        fixture.Vm.PasteFrameReader.SerialReader = path =>
            differentBody && path == target.FilePath ? "other-body" : Serial;
        await fixture.CopyAsync(source);
        await fixture.SelectAsync(target);
        Choose(fixture.Vm);
        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);

        if (differentBody)
        {
            Assert.Equal(before, EditSettingsJson.Serialize(target.EditSettings));
            Assert.Contains("different camera body", fixture.Vm.TransientStatus);
            Assert.Empty((await fixture.Catalog.LoadEditHistoryAsync(target.CatalogId)).Entries);
        }
        else
        {
            Assert.Equal(source.EditSettings.Repairs, target.EditSettings.Repairs);
            Assert.Equal(1, (await fixture.Catalog.LoadEditHistoryAsync(target.CatalogId)).Position);
        }
    }

    private static void Remember(MainWindowViewModel vm, ImageFile file, int width = 16, string model = "EOS 6D", bool monochrome = false)
    {
        vm.PasteFrameReader.RememberCamera(file, new(new CameraIdentity("Canon", model), monochrome));
        vm.PasteFrameReader.RememberSensorFrame(file, new(width, 12, 1));
    }

    private static void Choose(MainWindowViewModel vm) => vm.RestorePasteGroups(
        EditSettingsTransfer.Groups.ToDictionary(group => group.Name, group => group.Name == "Spot Removal"));
}
