using System.Text.Json.Nodes;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RepairFlowTests(ITestOutputHelper output)
{
    [Fact]
    // Frozen G2 control and allowance: docs/pipeline/TESTING.md#heal-wp2-frozen-controls (section 5).
    public async Task S64HistoryGrowthStaysWithinTheFrozenAllowance()
    {
        var spots = RepairModelTests.S64();
        var measured = await HealHistoryWorkload.RunAsync((settings, step) =>
            (settings.Repairs ??= []).Add(spots[step - 1]));
        output.WriteLine($"G2 S64: {measured}; frozen control={HealHistoryWorkload.FrozenControlHistoryBytes}; threshold=432938");
        Assert.Equal(65, measured.Rows);
        Assert.InRange(measured.HistoryBytes, 1, HealHistoryWorkload.FrozenControlHistoryBytes + 400000);
    }

    [Fact]
    public async Task PresetSaveAndImportExcludeRepairsAndPastePreservesDestination()
    {
        using var folder = new TemporaryDirectory();
        var service = new PresetService(folder.Path);
        await service.InitializeAsync();
        var source = new EditSettings { Exposure = 1, Repairs = RepairModelTests.S64() };
        var destination = new EditSettings { Repairs = [new()] };
        var repairs = destination.Repairs;
        var saved = await service.SaveUserPresetAsync("Repair exclusion", source);
        Assert.Null(saved.Settings.Repairs);
        Assert.Equal(64, source.Repairs.Count);
        var path = Path.Combine(folder.Path, $"{saved.Id}.json");
        var node = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        Assert.Null(node["settings"]!["repairs"]);
        node["settings"]!["repairs"] = "invalid payload ignored before validation";
        var imported = PresetService.DeserializePresetFile(node.ToJsonString(), path)!;
        Assert.Null(imported.Settings.Repairs);
        EditSettingsTransfer.ApplySubset(imported.Settings, destination);
        Assert.Same(repairs, destination.Repairs);
        EditSettingsTransfer.ApplySubset(source, destination);
        Assert.Same(repairs, destination.Repairs);
        Assert.Null(EditSettingsTransfer.CopySubset(source).Repairs);
        Assert.Equal(1, destination.Exposure);
    }

    [Fact]
    public void SpotLabelsDistinguishChangesAndAcceptGestureOperations()
    {
        var before = new EditSettings();
        var after = new EditSettings { Repairs = [new()] };
        Assert.Equal("Add Heal spot", EditHistoryLabel.Derive(before, after));
        after.Repairs![0].Type = "clone";
        Assert.Equal("Add Clone spot", EditHistoryLabel.Derive(before, after));
        Assert.Equal("Delete spot", EditHistoryLabel.Derive(after, before));
        foreach (var (property, label) in new[] { ("U", "Move spot"), ("V", "Move spot"),
            ("Su", "Move spot source"), ("Sv", "Move spot source"), ("Radius", "Spot size"),
            ("Feather", "Spot feather"), ("Opacity", "Spot opacity") })
        {
            var changed = after.Clone();
            typeof(Repair).GetProperty(property)!.SetValue(changed.Repairs![0], .02);
            Assert.Equal(label, EditHistoryLabel.Derive(after, changed));
        }
        var mode = after.Clone(); mode.Repairs![0].Type = "heal";
        Assert.Equal("Spot mode", EditHistoryLabel.Derive(after, mode));
        foreach (var operation in new[] { EditHistoryLabel.ResizeSpot, EditHistoryLabel.NewSpotSource, EditHistoryLabel.ClearSpots })
            Assert.Equal(operation, EditHistoryLabel.Derive(after, mode, operation));
        after.Repairs.Add(new());
        Assert.Equal("Clear spots", EditHistoryLabel.Derive(after, before));
        var deleted = after.Clone(); deleted.Repairs!.RemoveAt(0);
        Assert.Equal("Delete spot", EditHistoryLabel.Derive(after, deleted));
    }
}
