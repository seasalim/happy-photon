using System.Reflection;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SyncTransferSnapshotTests
{
    [AvaloniaFact]
    public async Task CopyKeepsAnIsolatedWholeDocument()
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var settings = SyncTransferParityCorpus.CreateLook();

        var destinations = SyncTransferParityCorpus.Destinations().ToDictionary(item => item.Name, item => item.Settings);
        settings.Locals = destinations["locals"].Locals;
        settings.Crop = destinations["crop"].Crop;
        settings.HorizonRotation = destinations["crop"].HorizonRotation;
        settings.Geometry = destinations["geometry"].Geometry;
        settings.RawProfile = destinations["raw-profile"].RawProfile;
        settings.Lens.ProfileOverride = destinations["lens"].Lens.ProfileOverride;
        settings.Rotation = 90;
        settings.Repairs = [new Repair { Id = new string('b', 32), Radius = .02 }];
        var source = await fixture.ImageAsync("copy-source", settings);
        await fixture.CopyAsync(source);
        var field = typeof(MainWindowViewModel).GetField("_copiedSettings", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var snapshot = Assert.IsType<EditSettings>(field.GetValue(fixture.Vm));
        var expected = EditSettingsJson.Serialize(source.EditSettings);
        Assert.Equal(expected, EditSettingsJson.Serialize(snapshot));
        Assert.NotSame(source.EditSettings, snapshot);

        source.EditSettings.Locals![0].Exposure = 3;
        source.EditSettings.Repairs![0].Radius = .05;
        source.EditSettings.Crop!.Left = .3;
        source.EditSettings.Geometry!.Vertical = 42;
        source.EditSettings.RawProfile!.Location = "another.dcp";
        source.EditSettings.Lens.ProfileOverride = "another lens";
        Assert.Equal(expected, EditSettingsJson.Serialize(snapshot));
    }

    [AvaloniaFact]
    public async Task PastePreservesPendingLensChoice()
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var source = await fixture.ImageAsync("source", SyncTransferParityCorpus.CreateLook());
        await fixture.CopyAsync(source);
        var target = await fixture.ImageAsync("target", new EditSettings());
        await fixture.SelectAsync(target);
        fixture.Vm.LensProfileOverride = "pending lens";
        Assert.Null(target.EditSettings.Lens.ProfileOverride);

        await fixture.Vm.PasteEditSettingsCommand.ExecuteAsync(null);

        Assert.Equal("pending lens", fixture.Vm.LensProfileOverride);
    }
}
