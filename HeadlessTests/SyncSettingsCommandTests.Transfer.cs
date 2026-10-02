using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class SyncSettingsCommandTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SyncExcludesSourceAndLeavesCopyBufferAlone(bool copyFirst)
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var copied = await fixture.ImageAsync("A", new() { Exposure = -.5 });

        if (copyFirst)
        {
            await fixture.CopyAsync(copied);
        }

        var source = await fixture.ImageAsync("B", new() { Exposure = 1 });
        var targets = new List<ImageFile>();

        for (var index = 0; index < 11; index++)
        {
            targets.Add(await fixture.ImageAsync($"target-{index}", new()));
        }

        var vm = fixture.Vm;
        SelectForSync(vm, [source, .. targets]);
        var buffer = CopiedFields(vm);
        var changes = 0;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.HasCopiedSettings)) changes++;
        };
        vm.ShowPasteSettingsAsync = dialog =>
        {
            Assert.Equal(PasteSettingsMode.Sync, dialog.Mode);
            Assert.Equal("Sync Settings", dialog.WindowTitle);
            Assert.Equal("Sync settings", dialog.Heading);
            Assert.Equal("Sync", dialog.PrimaryLabel);
            Assert.Equal("From B.jpg to 11 photos", dialog.Summary);
            Assert.Equal(11, dialog.TargetCount);

            return Task.FromResult(true);
        };
        Assert.Equal("Sync settings from B.jpg to 11 photos (Ctrl+Shift+S)", vm.SyncSettingsToolTip);
        await vm.SyncSettingsCommand.ExecuteAsync(null);
        Assert.Equal(copyFirst, vm.HasCopiedSettings);
        Assert.Equal(0, changes);
        Assert.All(buffer, pair => Assert.Same(pair.Value, CopiedFields(vm)[pair.Key]));
        Assert.Equal(1, source.EditSettings.Exposure);
        Assert.Empty((await fixture.Catalog.LoadEditHistoryAsync(source.CatalogId)).Entries);
        Assert.All(targets, target => Assert.Equal(1, target.EditSettings.Exposure));
        var stored = await fixture.Catalog.LoadImageStatesAsync([source.FilePath]);
        Assert.Equal(1, Assert.Single(stored[source.FilePath]).EditSettings.Exposure);
        Assert.Equal(copyFirst, vm.PasteEditSettingsCommand.CanExecute(null));

        if (copyFirst)
        {
            var target = await fixture.ImageAsync("paste-after-sync", new());
            await fixture.SelectAsync(target);
            await vm.PasteEditSettingsCommand.ExecuteAsync(null);
            Assert.Equal(-.5, target.EditSettings.Exposure);
        }
    }

    [AvaloniaFact]
    public async Task ConfirmedSyncGroupsDrivePasteTooltipAndNextDevelopPaste()
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var copied = await fixture.ImageAsync("A", new() { Exposure = -.5, Saturation = 19 });
        await fixture.CopyAsync(copied);
        var source = await fixture.ImageAsync("B", new() { Exposure = 1, Saturation = 25 });
        var target = await fixture.ImageAsync("target", new());
        var vm = fixture.Vm;
        SelectForSync(vm, source, target);
        var saved = 0;
        vm.PersistAppSettingsAsync = () =>
        {
            saved++;

            return Task.CompletedTask;
        };
        vm.ShowPasteSettingsAsync = dialog =>
        {
            dialog.NoneCommand.Execute(null);
            dialog.Groups.Single(group => group.Group.Name == "Adjustments").IsSelected = true;

            return Task.FromResult(true);
        };
        await vm.SyncSettingsCommand.ExecuteAsync(null);
        Assert.Equal(1, saved);
        Assert.Equal("Adjustments", vm.PasteSettingsTooltip.Split('\n')[1]);
        Assert.Equal(1, target.EditSettings.Exposure);
        Assert.Equal(0, target.EditSettings.Saturation);
        var develop = await fixture.ImageAsync("develop", new() { Saturation = -20 });
        await fixture.SelectAsync(develop);
        vm.ShowPasteSettingsAsync = _ => throw new InvalidOperationException("One-step paste must not open a dialog.");
        await vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.Equal(-.5, develop.EditSettings.Exposure);
        Assert.Equal(-20, develop.EditSettings.Saturation);
    }

    [AvaloniaTheory]
    [InlineData("cancel")]
    [InlineData("escape")]
    public async Task CancelAndEscapeLeaveGroupsTooltipAndDocumentsUnchanged(string action)
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var source = await fixture.ImageAsync("source", new() { Exposure = 1 });
        var target = await fixture.ImageAsync("target", new());
        var vm = fixture.Vm;
        SelectForSync(vm, source, target);
        var choice = vm.CapturePasteGroups();
        var tooltip = vm.PasteSettingsTooltip;
        var shown = false;
        using var owner = new TestUiScope(new Window());
        vm.PersistAppSettingsAsync = () => throw new InvalidOperationException("Cancel must not persist groups.");
        vm.ShowPasteSettingsAsync = async model =>
        {
            shown = true;
            var dialog = new PasteSettingsDialog(model);
            var result = dialog.ShowDialog<bool>(owner.Window!);

            try
            {
                model.AllCommand.Execute(null);
                Assert.Equal("Sync Settings", dialog.Title);
                Assert.Equal("Sync", dialog.FindControl<Button>("PasteButton")!.Content);

                if (action == "escape")
                {
                    dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
                }
                else
                {
                    dialog.FindControl<Button>("CancelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }

                return await result.WaitAsync(TestWaits.Condition);
            }
            finally
            {
                dialog.Close(false);
            }
        };
        await vm.SyncSettingsCommand.ExecuteAsync(null);
        Assert.True(shown);
        Assert.Equal(choice, vm.CapturePasteGroups());
        Assert.Equal(tooltip, vm.PasteSettingsTooltip);
        Assert.Equal(0, target.EditSettings.Exposure);
        Assert.Empty((await fixture.Catalog.LoadEditHistoryAsync(target.CatalogId)).Entries);
        Assert.Empty((await fixture.Catalog.LoadEditHistoryAsync(source.CatalogId)).Entries);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SyncAndCopyPasteHaveIdenticalDocumentsHistorySkipsAndThumbnailRefresh(bool allGroups)
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var settings = SyncTransferParityCorpus.CreateLook();
        settings.Locals = [new LocalAdjustment { Exposure = .5 }];
        settings.Geometry = new() { Vertical = 12 };
        settings.Repairs = [new Repair { Radius = .02 }];
        var source = await fixture.ImageAsync("source", settings);
        var syncTargets = new List<ImageFile>();
        var pasteTargets = new List<ImageFile>();

        foreach (var destination in SyncTransferParityCorpus.Destinations())
        {
            // All targets are standard sources, keeping their own photo-specific state.
            var original = destination.Settings.Clone();
            original.RawProfile = null;
            syncTargets.Add(await fixture.ImageAsync("sync-" + destination.Name, original));
            pasteTargets.Add(await fixture.ImageAsync("paste-" + destination.Name, original));
        }

        using var pixels = new MagickImage(MagickColors.Gray, 16, 12);

        foreach (var target in syncTargets.Concat(pasteTargets))
        {
            await pixels.WriteAsync(target.FilePath);
        }

        var vm = fixture.Vm;
        SelectForSync(vm, [source, .. syncTargets]);
        vm.ShowPasteSettingsAsync = dialog =>
        {
            if (allGroups) dialog.AllCommand.Execute(null);

            return Task.FromResult(true);
        };
        await vm.SyncSettingsCommand.ExecuteAsync(null);
        var report = vm.TransientStatus;
        await TestWaits.UntilAsync(() => vm.DirectThumbnailActivityCount == 0);
        Assert.All(syncTargets, target => Assert.NotNull(target.Thumbnail));
        Assert.Empty((await fixture.Catalog.LoadEditHistoryAsync(source.CatalogId)).Entries);
        vm.CopyEditSettingsCommand.Execute(null);
        vm.Browse.SetImages(pasteTargets);
        vm.Browse.SelectAllVisible();
        await vm.PasteEditSettingsCommand.ExecuteAsync(null);
        await TestWaits.UntilAsync(() => vm.DirectThumbnailActivityCount == 0);
        Assert.Equal(report, vm.TransientStatus);
        Assert.All(pasteTargets, target => Assert.NotNull(target.Thumbnail));

        foreach (var (sync, paste) in syncTargets.Zip(pasteTargets))
        {
            Assert.Equal(EditSettingsJson.Serialize(paste.EditSettings), EditSettingsJson.Serialize(sync.EditSettings));
            var syncHistory = await fixture.Catalog.LoadEditHistoryAsync(sync.CatalogId);
            var pasteHistory = await fixture.Catalog.LoadEditHistoryAsync(paste.CatalogId);
            Assert.Equal(pasteHistory.Position, syncHistory.Position);
            Assert.Equal(pasteHistory.Entries.Select(entry => (entry.Label, EditSettingsJson.Serialize(entry.Settings))),
                syncHistory.Entries.Select(entry => (entry.Label, EditSettingsJson.Serialize(entry.Settings))));
        }

        if (allGroups)
        {
            Assert.Contains("Spot Removal kept", report);
        }
    }

    [AvaloniaFact]
    public async Task SyncReframeCountUsesItsOwnSnapshotWithoutSourceReads()
    {
        var availability = new TestSourceAvailabilityService(SourceAvailability.AvailableLocally);
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync(availability: availability);
        var copied = await fixture.ImageAsync("copied-no-crop", new());
        await fixture.CopyAsync(copied);
        var source = await fixture.ImageAsync("sync-crop", new() { Crop = new() { Left = .2 } });
        var target = await fixture.ImageAsync("target", new());

        using (var pixels = new MagickImage(MagickColors.Gray, 16, 12))
        {
            await pixels.WriteAsync(source.FilePath);
        }

        using (var pixels = new MagickImage(MagickColors.Gray, 12, 16))
        {
            await pixels.WriteAsync(target.FilePath);
        }

        var vm = fixture.Vm;
        // Seed resident header facts before the command; the dialog may only use this cache.
        Assert.NotNull(vm.PasteFrameReader.Read(source, source.EditSettings));
        Assert.NotNull(vm.PasteFrameReader.Read(target, target.EditSettings));
        SelectForSync(vm, source, target);
        var reads = availability.CallCount;
        var shown = false;
        vm.ShowPasteSettingsAsync = dialog =>
        {
            shown = true;
            Assert.Equal("From sync-crop.jpg to 1 photo", dialog.Summary);
            Assert.Contains("reframed to fit on 1", dialog.Groups.Single(group => group.Group.Name == "Crop & Straighten").Note);
            Assert.Equal(reads, availability.CallCount);

            return Task.FromResult(false);
        };
        await vm.SyncSettingsCommand.ExecuteAsync(null);
        Assert.True(shown);
        Assert.Null(target.EditSettings.Crop);
    }

    private static Dictionary<string, object?> CopiedFields(MainWindowViewModel vm) =>
        new[] { "_copiedSettings", "_copiedSource", "_copiedSourceName", "_copiedProfileSource", "_copiedSpotSource" }
            .ToDictionary(name => name, name => typeof(MainWindowViewModel)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm));
}
