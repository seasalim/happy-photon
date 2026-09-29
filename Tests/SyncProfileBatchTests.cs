using System.Diagnostics;
using System.Security.Cryptography;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class SyncProfileBatchTests(ITestOutputHelper output)
{
    [Fact]
    public async Task AC1_150SameCamera30CompatibleMount20IncompatibleHaveExactHistoryAndCounts()
    {
        await RunAsync("cold", measure: false);
    }

    [Theory]
    [InlineData("warm")]
    [InlineData("automatic")]
    public async Task G2_FactsInMemoryOrNeutralSelectionsHaveZeroOpensAndDecodes(string mode)
    {
        await RunAsync(mode, measure: false);
    }

    [Fact]
    public async Task G1_ColdProfilePasteAddedCostIsWithinHeaderControl()
    {
        SyncPhotoGateSupport.RequirePerformance();
        await RunAsync("cold", measure: true, includeLook: true);
    }

    [Fact]
    public async Task G1_MatchedArmsPreserveLookAndProfileTransferChecks()
    {
        await RunAsync("cold", measure: false, includeLook: true);
    }

    private async Task RunAsync(string mode, bool measure, bool includeLook = false)
    {
        using var fixture = new CatalogVmFixture("sync-profiles");
        using var catalog = await fixture.CreateCatalogAsync();
        var originals = SyncProfileGateSupport.ConfirmFixtures();
        var paths = SyncProfileGateSupport.CopyTargets(fixture.Root, originals);
        var events = new CullPerfRecorder();
        var loader = new BaseLoaderRouter(new RawBaseLoader { CullPerf = events },
            new StandardBaseLoader { CullPerf = events });
        await using var vm = fixture.CreateViewModel(catalog, loader, _ => Task.CompletedTask,
            new SourceAvailabilityService(), timeProvider: new TestTimeProvider());
        vm.ImageService.Previews.AdjacentWarmEnabled = false;
        var dcp = SyntheticDcpFactory.WriteTemporary(fixture.Root, new SyntheticDcpOptions
        {
            Name = "Sync profile control", UniqueCameraModel = "Canon EOS 6D"
        });
        var profile = new RawProfileSelection
        {
            Source = RawProfileSource.UserFile, Location = dcp,
            ContentHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dcp))).ToLowerInvariant()
        };
        var sourceSettings = includeLook ? SyncPhotoBatchGateTests.CreateLook() : new EditSettings();
        sourceSettings.RawProfile = mode == "automatic" ? null : profile;
        sourceSettings.Lens.ProfileOverride = mode == "automatic" ? null : SyncProfileGateSupport.Lens;
        var source = new ImageFile(originals[0]) { EditSettings = sourceSettings };
        var before = new EditSettings();

        if (mode == "automatic")
        {
            before.RawProfile = profile.Clone();
            before.Lens.ProfileOverride = SyncProfileGateSupport.Lens;
        }

        var targets = new List<ImageFile>();

        foreach (var path in paths)
        {
            var target = new ImageFile(path)
            {
                CatalogId = await catalog.GetOrCreateImageAsync(path), EditSettings = before.Clone()
            };
            await catalog.SaveEditSettingsAsync(target.CatalogId, target.EditSettings);
            targets.Add(target);

            if (mode == "warm") Assert.NotNull(vm.PasteFrameReader.ReadCamera(target));
        }

        var headerMs = 0d;
        var controlMs = 0d;

        if (measure)
        {
            var header = new LibRawProcessingService();
            var times = new List<double>();

            foreach (var path in paths)
            {
                var start = Stopwatch.GetTimestamp();
                Assert.NotNull(header.ExtractMetadata(path));
                times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }

            headerMs = SyncProfileGateSupport.Median(times);
            Assert.InRange(headerMs, 1, 40);
        }

        if (includeLook)
        {
            // Both arms share source values and frozen target documents; only the profile groups differ.
            var look = sourceSettings.Clone();
            look.RawProfile = before.RawProfile?.Clone();
            look.Lens.ProfileOverride = before.Lens.ProfileOverride;
            using var controlCatalog = await fixture.CreateCatalogAsync("control");
            await using var controlVm = fixture.CreateViewModel(controlCatalog, loader, _ => Task.CompletedTask,
                new SourceAvailabilityService(), timeProvider: new TestTimeProvider());
            controlVm.ImageService.Previews.AdjacentWarmEnabled = false;
            var controlTargets = new List<ImageFile>();

            foreach (var path in paths)
            {
                var target = new ImageFile(path)
                {
                    CatalogId = await controlCatalog.GetOrCreateImageAsync(path), EditSettings = before.Clone()
                };
                await controlCatalog.SaveEditSettingsAsync(target.CatalogId, target.EditSettings);
                controlTargets.Add(target);
            }

            controlVm.SelectedImage = new ImageFile(source.FilePath) { EditSettings = sourceSettings.Clone() };
            controlVm.CopyEditSettingsCommand.Execute(null);
            controlVm.Browse.SetImages(controlTargets);
            controlVm.Browse.SelectAllVisible();
            controlVm.RestorePasteGroups(EditSettingsTransfer.Groups.ToDictionary(group => group.Name,
                group => group.Kind == EditSettingsGroupKind.Look));
            var timer = new Stopwatch();
            controlVm.ShowPasteSettingsAsync = _ =>
            {
                timer.Start();

                return Task.FromResult(true);
            };
            await controlVm.PasteEditSettingsCommand.ExecuteAsync(null);
            timer.Stop();
            controlMs = timer.Elapsed.TotalMilliseconds;

            foreach (var target in controlTargets)
            {
                Assert.Equal(EditSettingsJson.Serialize(look), EditSettingsJson.Serialize(target.EditSettings));
                var history = await controlCatalog.LoadEditHistoryAsync(target.CatalogId);
                Assert.Equal(1, history.Position);
                Assert.Equal(EditSettingsJson.Serialize(before), EditSettingsJson.Serialize(history.Entries[0].Settings));
            }
        }

        vm.SelectedImage = source;
        vm.CopyEditSettingsCommand.Execute(null);
        vm.Browse.SetImages(targets);
        vm.Browse.SelectAllVisible();
        vm.RestorePasteGroups(EditSettingsTransfer.Groups.ToDictionary(group => group.Name,
            group => includeLook && group.Kind == EditSettingsGroupKind.Look ||
                group.Name is "Camera Profile" or "Lens Profile"));
        var opens = new List<string>();
        vm.PasteFrameReader.Opening = (path, _) => opens.Add(path);
        var watch = new Stopwatch();
        vm.ShowPasteSettingsAsync = _ =>
        {
            watch.Start();

            return Task.FromResult(true);
        };
        await vm.PasteEditSettingsCommand.ExecuteAsync(null);
        watch.Stop();
        Assert.Equal(mode == "cold" ? 200 : 0, opens.Count);
        Assert.Equal(opens.Count, opens.Distinct().Count());
        Assert.DoesNotContain(source.FilePath, opens);
        Assert.Equal(0, events.LostEvents);
        Assert.DoesNotContain(events.Snapshot(), item => item.Kind == "NativeStart");
        var changed = includeLook || mode == "automatic" ? 200 : 180;
        Assert.StartsWith($"Applied to {changed} photos", vm.TransientStatus);

        if (mode != "automatic")
        {
            Assert.Contains("Camera Profile kept on 50 (different camera)", vm.TransientStatus);
            Assert.Contains("Lens Profile kept on 20 (outside the lens mount)", vm.TransientStatus);
        }

        var persisted = await catalog.LoadImageStatesAsync(paths);

        for (var index = 0; index < targets.Count; index++)
        {
            var target = targets[index];
            Assert.Equal(mode != "automatic" && index < 150 ? profile.ContentHash : null,
                target.EditSettings.RawProfile?.ContentHash);
            Assert.Equal(mode != "automatic" && index < 180 ? SyncProfileGateSupport.Lens : null,
                target.EditSettings.Lens.ProfileOverride);
            var expected = sourceSettings.Clone();
            expected.RawProfile = mode != "automatic" && index < 150 ? profile.Clone() : null;
            expected.Lens.ProfileOverride = mode != "automatic" && index < 180 ? SyncProfileGateSupport.Lens : null;
            Assert.Equal(EditSettingsJson.Serialize(expected), EditSettingsJson.Serialize(target.EditSettings));
            Assert.Equal(EditSettingsJson.Serialize(target.EditSettings),
                EditSettingsJson.Serialize(Assert.Single(persisted[target.FilePath]).EditSettings));
            var history = await catalog.LoadEditHistoryAsync(target.CatalogId);

            if (index < changed)
            {
                Assert.Equal(1, history.Position);
                Assert.Equal(new[] { "Original", "Paste settings" }, history.Entries.Select(entry => entry.Label));
                Assert.Equal(EditSettingsJson.Serialize(before), EditSettingsJson.Serialize(history.Entries[0].Settings));
                Assert.Equal(EditSettingsJson.Serialize(target.EditSettings), EditSettingsJson.Serialize(history.Entries[1].Settings));
            }
            else
            {
                Assert.Empty(history.Entries);
                Assert.Equal(EditSettingsJson.Serialize(before), EditSettingsJson.Serialize(target.EditSettings));
            }
        }

        var added = (watch.Elapsed.TotalMilliseconds - controlMs) / 200;
        output.WriteLine($"SYNC_PROFILE_TRANSFER mode={mode} pasteMs={watch.Elapsed.TotalMilliseconds:R} controlMs={controlMs:R} headerMs={headerMs:R} addedMs={added:R} readerOpens={opens.Count} baseDecodes=0 changed={changed} history={changed}");

        if (measure) Assert.True(added <= headerMs * 1.25 + 1, $"Added {added} ms; control {headerMs} ms");
    }
}
