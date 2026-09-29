using System.Reflection;
using System.Runtime.CompilerServices;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class SyncProfileRefreshTests
{
    [Fact]
    public async Task AC4_RefreshesOnlyChangedRawTargetsWithRenderedCacheEntries()
    {
        using var fixture = new CatalogVmFixture("profile-refresh");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(catalog, new NullBaseLoader(), _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally), timeProvider: new TestTimeProvider());
        var profile = SyncProfileTransferTests.Profile();
        var source = new ImageFile(fixture.Path("source.cr2")) { EditSettings = new() { RawProfile = profile } };
        var facts = new PhotoCameraFacts(new("Canon", "EOS 6D"), false);
        vm.PasteFrameReader.RememberCamera(source, facts);
        vm.SelectedImage = source;
        vm.CopyEditSettingsCommand.Execute(null);
        var targets = new List<ImageFile>();

        for (var index = 0; index < 4; index++)
        {
            var target = new ImageFile(fixture.Path($"target-{index}.cr2"))
            {
                CatalogId = await catalog.GetOrCreateImageAsync(fixture.Path($"target-{index}.cr2"))
            };
            if (index == 1) target.EditSettings.RawProfile = profile.Clone();

            vm.PasteFrameReader.RememberCamera(target, index == 2 ? new(new("Nikon", "D70"), false) : facts);
            targets.Add(target);

            if (index < 3)
            {
                var cache = catalog.GetRenderedThumbnailPath(target.CatalogId);
                Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
                await File.WriteAllTextAsync(Path.ChangeExtension(cache, ".meta"), "old rendered entry");
                Assert.True(vm.ImageService.Thumbnails.HasRenderedCacheEntry(target));
            }
        }

        vm.Browse.SetImages(targets);
        vm.Browse.SelectAllVisible();
        vm.RestorePasteGroups(EditSettingsTransfer.Groups.ToDictionary(group => group.Name,
            group => group.Name == "Camera Profile"));
        vm.ShowPasteSettingsAsync = _ => Task.FromResult(true);
        var requests = (ConditionalWeakTable<ImageFile, StrongBox<long>>)typeof(MainWindowViewModel)
            .GetField("_thumbnailRequests", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(vm)!;
        var previous = targets.Select(target => requests.GetOrCreateValue(target).Value).ToArray();
        await vm.PasteEditSettingsCommand.ExecuteAsync(null);
        await TestWaits.UntilAsync(() => vm.DirectThumbnailActivityCount == 0);
        Assert.Equal(new long[] { 1, 0, 0, 0 }, targets.Select((target, index) =>
            requests.GetOrCreateValue(target).Value - previous[index]));
        Assert.Equal(profile.ContentHash, targets[3].EditSettings.RawProfile?.ContentHash);
        Assert.Null(targets[2].EditSettings.RawProfile);
    }
    [Fact]
    public async Task AC2_EmbeddedProfileTransfersToClearedVersionButKeepsAnotherDngUnchanged()
    {
        using var fixture = new CatalogVmFixture("embedded-sync");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(catalog, new NullBaseLoader(), _ => Task.CompletedTask,
            new SourceAvailabilityService(), timeProvider: new TestTimeProvider());
        var sourcePath = fixture.Path("source.dng");
        var otherPath = fixture.Path("other.dng");
        await File.WriteAllBytesAsync(sourcePath, SyntheticDcpFactory.Create(new() { Name = "Source embedded" }));
        await File.WriteAllBytesAsync(otherPath, SyntheticDcpFactory.Create(new() { Name = "Other embedded" }));
        var reader = new DcpProfileReader();
        var source = new ImageFile(sourcePath)
        {
            CatalogId = await catalog.GetOrCreateImageAsync(sourcePath),
            EditSettings = new()
            {
                RawProfile = new()
                {
                    Source = RawProfileSource.Embedded,
                    ContentHash = Assert.Single(reader.ReadEmbeddedProfiles(sourcePath)).ContentHash
                }
            }
        };
        await catalog.SaveEditSettingsAsync(source.CatalogId, source.EditSettings);
        var versionState = Assert.IsType<CatalogImageState>(await catalog.CreateVersionAsync(source.CatalogId));
        var version = new ImageFile(sourcePath) { CatalogId = versionState.CatalogId, Version = 2 };
        await catalog.SaveEditSettingsAsync(version.CatalogId, version.EditSettings);
        var other = new ImageFile(otherPath)
        {
            CatalogId = await catalog.GetOrCreateImageAsync(otherPath),
            EditSettings = new()
            {
                AppliedPresetId = "keep-marker",
                RawProfile = new()
                {
                    Source = RawProfileSource.Embedded,
                    ContentHash = Assert.Single(reader.ReadEmbeddedProfiles(otherPath)).ContentHash
                }
            }
        };
        await catalog.SaveEditSettingsAsync(other.CatalogId, other.EditSettings);
        var before = EditSettingsJson.Serialize(other.EditSettings);
        vm.PasteFrameReader.RememberCamera(source, new(new("Pentax", "K-r"), false));
        vm.SelectedImage = source;
        vm.CopyEditSettingsCommand.Execute(null);
        vm.PasteFrameReader.Opening = (_, _) => Assert.Fail("Embedded transfer does not read identity.");
        vm.Browse.SetImages([version, other]);
        vm.Browse.SelectAllVisible();
        vm.RestorePasteGroups(EditSettingsTransfer.Groups.ToDictionary(group => group.Name,
            group => group.Name == "Camera Profile"));
        vm.ShowPasteSettingsAsync = _ => Task.FromResult(true);
        await vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.Equal(source.EditSettings.RawProfile.ContentHash, version.EditSettings.RawProfile?.ContentHash);
        Assert.Equal(before, EditSettingsJson.Serialize(other.EditSettings));
        Assert.Empty((await catalog.LoadEditHistoryAsync(other.CatalogId)).Entries);
        Assert.Equal(1, (await catalog.LoadEditHistoryAsync(version.CatalogId)).Position);
        Assert.Contains("Camera Profile kept on 1 (embedded in another photo)", vm.TransientStatus);
    }

}
