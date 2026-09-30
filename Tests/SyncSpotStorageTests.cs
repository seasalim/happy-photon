using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class SyncSpotStorageTests(ITestOutputHelper output)
{
    [Fact]
    public Task G3_DefaultGroupsControl() => Measure(false);

    [Fact]
    public Task G3_SpotRemoval() => Measure(true);

    private async Task Measure(bool spots)
    {
        SyncPhotoGateSupport.RequirePerformance();
        var payloadBytes = SyncSpotGateSupport.PayloadBytes(output);
        var original = SyncSpotGateSupport.Fixture();
        using var fixture = new CatalogVmFixture("sync-spot-growth");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(catalog, new NullBaseLoader(), _ => Task.CompletedTask,
            new SourceAvailabilityService(), timeProvider: new TestTimeProvider());
        vm.ImageService.Previews.AdjacentWarmEnabled = false;
        var expected = spots ? SyncSpotGateSupport.Source() : SyncPhotoBatchGateTests.CreateLook();
        var sourceSettings = expected.Clone();
        sourceSettings.Repairs = RepairTestWorkload.S64();
        var source = new ImageFile(original) { EditSettings = sourceSettings };
        var before = new EditSettings();
        Assert.Null(before.AppliedPresetId);
        Assert.Null(sourceSettings.AppliedPresetId);
        var frozenBefore = EditSettingsJson.Serialize(before);
        var frozenExpected = EditSettingsJson.Serialize(expected);
        var targets = new List<ImageFile>();

        foreach (var group in EditSettingsTransfer.Groups.Where(group => spots ? group.Name == "Spot Removal" : group.IsDefault))
        {
            var isolated = before.Clone();
            EditSettingsTransfer.ApplyGroups(sourceSettings, isolated, [group]);
            Assert.NotEqual(frozenBefore, EditSettingsJson.Serialize(isolated));
        }

        for (var index = 0; index < 200; index++)
        {
            var path = fixture.Path($"target-{index:D3}.cr2");
            SyncProfileGateSupport.RequireLocal(original);
            File.Copy(original, path);
            SyncProfileGateSupport.RequireLocal(path);
            SyncProfileGateSupport.AssertHash(path,
                "7727EE0280B44EA1D633962F49942F37F3C7EC6D704D22E108A5223666327C32");
            var target = new ImageFile(path)
            {
                CatalogId = await catalog.GetOrCreateImageAsync(path), EditSettings = before.Clone()
            };
            await catalog.SaveEditSettingsAsync(target.CatalogId, target.EditSettings);
            Assert.Empty((await catalog.LoadEditHistoryAsync(target.CatalogId)).Entries);
            Assert.Equal(frozenBefore, EditSettingsJson.Serialize(target.EditSettings));
            targets.Add(target);
        }

        vm.PasteFrameReader.Read(source, sourceSettings);
        vm.SelectedImage = source;
        vm.CopyEditSettingsCommand.Execute(null);
        vm.Browse.SetImages(targets);
        vm.Browse.SelectAllVisible();
        vm.RestorePasteGroups(EditSettingsTransfer.Groups.ToDictionary(group => group.Name, group => spots ? group.Name == "Spot Removal" : group.IsDefault));
        var confirmations = 0;
        vm.ShowPasteSettingsAsync = dialog =>
        {
            Assert.Equal(200, dialog.TargetCount);
            confirmations++;

            return Task.FromResult(true);
        };
        var opens = new List<(string Path, string Reason)>();
        vm.PasteFrameReader.Opening = (path, reason) => opens.Add((path, reason));
        var database = fixture.Path("catalog.db");
        await Checkpoint(database);
        var beforeBytes = new FileInfo(database).Length;
        await vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.Equal(1, confirmations);
        var firstOpens = opens.Count;
        Assert.Equal(spots ? 401 : 0, firstOpens);

        if (spots)
        {
            Assert.Equal(201, opens.Count(open => open.Reason == "body serial"));
            Assert.All(targets, target => Assert.Equal(2, opens.Count(open => open.Path == target.FilePath)));
        }

        foreach (var target in targets)
        {
            Assert.Equal(frozenExpected, EditSettingsJson.Serialize(target.EditSettings));
            Assert.Equal(spots ? 64 : 0, target.EditSettings.Repairs?.Count ?? 0);
            await SyncSpotGateSupport.AssertTransfer(catalog, target, before, expected);
        }

        await Checkpoint(database);
        var afterBytes = new FileInfo(database).Length;
        // Repeating an equal paste must not create another step. Kept outside the growth interval.
        await vm.PasteEditSettingsCommand.ExecuteAsync(null);

        foreach (var target in targets)
        {
            await SyncSpotGateSupport.AssertTransfer(catalog, target, before, expected);
        }

        Assert.Equal(firstOpens, opens.Count);
        Assert.Equal(frozenBefore, EditSettingsJson.Serialize(before));
        Assert.Equal(frozenExpected, EditSettingsJson.Serialize(expected));
        SyncPhotoGateSupport.LocalFixture(SyncPhotoGateSupport.Raw);
        if (spots) Assert.InRange(afterBytes - beforeBytes, 1, 6046040);

        output.WriteLine($"SYNC_SPOT gate=G3 pid={Environment.ProcessId} targets=200 beforeBytes={beforeBytes} afterBytes={afterBytes} growthBytes={afterBytes - beforeBytes} checkpoint=TRUNCATE historySteps=200 equalRepeatSteps=0 mode={(spots ? "spot-removal" : "default-groups-control")} thresholdFormulaBytes={200 * (2 * payloadBytes + 4096) * 1.10m}");
    }

    private static async Task Checkpoint(string path)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt32(0));
    }
}

