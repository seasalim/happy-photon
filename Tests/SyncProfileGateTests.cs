using System.Diagnostics;
using System.Security.Cryptography;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class SyncProfileGateTests(ITestOutputHelper output)
{
    [Fact]
    public void G1_HeaderControl()
    {
        SyncPhotoGateSupport.RequirePerformance();
        var root = GoldenTestPaths.RepositoryRoot;
        SyncProfileGateSupport.AssertHash(Path.Combine(root, "Services/LibRawProcessingService.cs"),
            "26E710E33CCA2899BA7EEAC4463236965034FD6DC895096605C162646CFBFADE");
        SyncProfileGateSupport.AssertHash(Path.Combine(root, "Interop/HappyPhoton.LibRaw.Interop/LibRawContext.cs"),
            "26236B8D347B07E6D2E7BED7C9A0655C4D0742B2C0CE15D29067FFFC8E54377E");
        using var directory = new TemporaryDirectory();
        var paths = SyncProfileGateSupport.CopyTargets(directory.Path, SyncProfileGateSupport.ConfirmFixtures());
        var raw = new LibRawProcessingService();
        Assert.True(raw.IsAvailable);
        var samples = new List<double>();

        // Cold per distinct target path and LibRaw context; no OS cache eviction is claimed.
        foreach (var path in paths)
        {
            SyncProfileGateSupport.RequireLocal(path);
            var start = Stopwatch.GetTimestamp();
            var metadata = raw.ExtractMetadata(path);
            samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            Assert.NotNull(metadata);
            Assert.True(metadata.PixelWidth > 0 && metadata.PixelHeight > 0);
        }

        var median = SyncProfileGateSupport.Median(samples);
        output.WriteLine($"SYNC_PROFILE headerMedianMs={median:R} files=200 pid={Environment.ProcessId}");
        Assert.InRange(median, 1, 40);
    }

    [Fact]
    public async Task DefaultGroupsControl()
    {
        SyncPhotoGateSupport.RequirePerformance();
        using var fixture = new CatalogVmFixture("sync-profile-control");
        var originals = SyncProfileGateSupport.ConfirmFixtures();
        var paths = SyncProfileGateSupport.CopyTargets(fixture.Root, originals);
        var profile = SyntheticDcpFactory.WriteTemporary(fixture.Root, new SyntheticDcpOptions
        {
            Name = "Sync profile control", UniqueCameraModel = "Canon EOS 6D"
        });
        using var catalog = await fixture.CreateCatalogAsync();
        var events = new CullPerfRecorder();
        var loader = new BaseLoaderRouter(new RawBaseLoader { CullPerf = events },
            new StandardBaseLoader { CullPerf = events });
        await using var vm = fixture.CreateViewModel(catalog, loader, _ => Task.CompletedTask,
            new SourceAvailabilityService(), timeProvider: new TestTimeProvider());
        vm.ImageService.Previews.AdjacentWarmEnabled = false;
        SyncProfileGateSupport.AssertHash(Path.Combine(GoldenTestPaths.RepositoryRoot, "Tests/SyncPhotoBatchGateTests.cs"),
            "5DCF892B19DC22BC6ADDC3409E3920C8B4D1254B00FAED5954D8B64F63DAB031");
        var profileReader = new DcpProfileReader();
        Assert.Equal("Canon EOS 6D", profileReader.ReadExternalUniqueCameraModel(profile));
        Assert.NotNull(profileReader.ParseExternal(profileReader.ReadExternalSnapshot(profile), "Sync profile control"));
        var look = SyncPhotoBatchGateTests.CreateLook();
        var sourceSettings = look.Clone();
        sourceSettings.RawProfile = new RawProfileSelection
        {
            Source = RawProfileSource.UserFile, Location = profile,
            ContentHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(profile))).ToLowerInvariant()
        };
        sourceSettings.Lens.ProfileOverride = SyncProfileGateSupport.Lens;
        var source = new ImageFile(originals[0]) { EditSettings = sourceSettings };
        var targets = new List<ImageFile>();
        var originalJson = EditSettingsJson.Serialize(new EditSettings());

        foreach (var path in paths)
        {
            var target = new ImageFile(path) { CatalogId = await catalog.GetOrCreateImageAsync(path) };
            await catalog.SaveEditSettingsAsync(target.CatalogId, target.EditSettings);
            Assert.Empty((await catalog.LoadEditHistoryAsync(target.CatalogId)).Entries);
            targets.Add(target);
        }

        foreach (var group in EditSettingsTransfer.DefaultGroups)
        {
            var isolated = new EditSettings();
            EditSettingsTransfer.ApplyGroups(sourceSettings, isolated, [group]);
            Assert.NotEqual(originalJson, EditSettingsJson.Serialize(isolated));
        }

        vm.SelectedImage = source;
        vm.CopyEditSettingsCommand.Execute(null);
        vm.Browse.SetImages(targets);
        vm.Browse.SelectAllVisible();
        vm.RestorePasteGroups(EditSettingsTransfer.Groups.ToDictionary(group => group.Name, group => group.IsDefault));
        var opens = 0;
        // IMPLEMENT: retain this hook for the combined identity/frame reader, counting every reason.
        vm.PasteFrameReader.Opening = (_, _) => Interlocked.Increment(ref opens);
        var timer = new Stopwatch();
        var confirmations = 0;
        vm.ShowPasteSettingsAsync = dialog =>
        {
            Assert.Equal(200, dialog.TargetCount);
            confirmations++;
            timer.Start();

            return Task.FromResult(true);
        };
        Assert.False(vm.IsDevelopMode);
        await vm.PasteEditSettingsCommand.ExecuteAsync(null);
        timer.Stop();
        Assert.Equal(1, confirmations);
        var decodes = events.Snapshot().Count(item => item.Kind == "NativeStart");
        Assert.Equal(0, events.LostEvents);
        Assert.Equal(0, opens);
        Assert.Equal(0, decodes);
        var expected = EditSettingsJson.Serialize(look);
        var stored = await catalog.LoadImageStatesAsync(paths);

        foreach (var target in targets)
        {
            Assert.Equal(expected, EditSettingsJson.Serialize(target.EditSettings));
            Assert.Equal(expected, EditSettingsJson.Serialize(Assert.Single(stored[target.FilePath]).EditSettings));
            var history = await catalog.LoadEditHistoryAsync(target.CatalogId);
            Assert.Equal(1, history.Position);
            Assert.Equal(new[] { "Original", "Paste settings" }, history.Entries.Select(entry => entry.Label));
            Assert.Equal(originalJson, EditSettingsJson.Serialize(history.Entries[0].Settings));
            Assert.Equal(expected, EditSettingsJson.Serialize(history.Entries[1].Settings));
        }

        await AssertFullySkippedAsync(fixture, catalog);
        output.WriteLine($"SYNC_PROFILE pasteMs={timer.Elapsed.TotalMilliseconds:R} readerOpens={opens} baseDecodes={decodes} transferred=200 checkedHistory=200 skippedHistory=0 pid={Environment.ProcessId}");
        output.WriteLine($"SYNC_PROFILE fixtures=150x{SyncProfileGateSupport.Names[0]},30x{SyncProfileGateSupport.Names[1]},20x{SyncProfileGateSupport.Names[2]} lens={SyncProfileGateSupport.Lens} dcpHash={sourceSettings.RawProfile.ContentHash}");
    }

    private static async Task AssertFullySkippedAsync(CatalogVmFixture fixture, CatalogService catalog)
    {
        // Current fully-skipped path: unavailable frame facts, crop only. Profile skips do not exist yet.
        await using var vm = fixture.CreateViewModel(catalog, new NullBaseLoader(), _ => Task.CompletedTask,
            new SourceAvailabilityService(), timeProvider: new TestTimeProvider());
        var source = new ImageFile(fixture.Path("missing-source.cr2"))
        {
            EditSettings = new() { Crop = new() { Left = .2 }, HorizonRotation = 2 }
        };
        var target = new ImageFile(fixture.Path("missing-target.cr2"))
        {
            EditSettings = new() { HorizonRotation = 1 }
        };
        target.CatalogId = await catalog.GetOrCreateImageAsync(target.FilePath);
        await catalog.SaveEditSettingsAsync(target.CatalogId, target.EditSettings);
        var expected = EditSettingsJson.Serialize(target.EditSettings);
        vm.SelectedImage = source;
        vm.CopyEditSettingsCommand.Execute(null);
        vm.Browse.SetImages([target]);
        vm.Browse.SelectAllVisible();
        vm.RestorePasteGroups(EditSettingsTransfer.Groups.ToDictionary(group => group.Name, group => group.Name == "Crop & Straighten"));
        vm.ShowPasteSettingsAsync = _ => Task.FromResult(true);
        await vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.Equal(expected, EditSettingsJson.Serialize(target.EditSettings));
        var stored = await catalog.LoadImageStatesAsync([target.FilePath]);
        Assert.Equal(expected, EditSettingsJson.Serialize(Assert.Single(stored[target.FilePath]).EditSettings));
        Assert.Empty((await catalog.LoadEditHistoryAsync(target.CatalogId)).Entries);
        Assert.Contains("facts unavailable", vm.TransientStatus);
    }
}

