using System.Text.Json;
using System.Text.Json.Nodes;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RepairModelTests
{
    [Fact]
    public void EmptyRepairsOmitAndS64RoundTripsBesideEightLocals()
    {
        var empty = new EditSettings();
        var original = EditSettingsJson.Serialize(empty);
        empty.Repairs = [];
        Assert.Equal(original, EditSettingsJson.Serialize(empty));
        Assert.Equal(RenderSettingsHash.Compute(new()), RenderSettingsHash.Compute(empty));
        Assert.Equal(S64(), RepairTestWorkload.S64());
        var settings = HealWorkloads.LH8();
        settings.Repairs = S64();
        var json = EditSettingsJson.Serialize(settings);
        var loaded = EditSettingsJson.Deserialize(json, out var clamped);
        Assert.False(clamped);
        Assert.Equal(json, EditSettingsJson.Serialize(loaded));
        Assert.Equal(settings.Repairs, loaded.Repairs);
        Assert.Equal(8, loaded.Locals!.Count);
        Assert.Equal(64, loaded.Repairs!.Count);
        Assert.Equal("repairs", JsonNode.Parse(json)!.AsObject().Last().Key);
        loaded.Locals.Add(new LocalAdjustment { Ordinal = 9 });
        Assert.Throws<JsonException>(() => EditSettingsJson.Serialize(loaded));
    }

    [Theory]
    [InlineData(64, 0.03061862178478972, 1, true)]
    [InlineData(6, .1, 1, true)]
    [InlineData(7, .1, 1, false)]
    [InlineData(64, 0.03061862178478972, 1.000000001, false)]
    public void AreaBoundaryVerdictsAgreeOnLoadAndSaveInShuffledOrder(
        int count, double radius, double scale, bool admitted)
    {
        Assert.Equal(0.18849555921538758, Repair.MaximumArea);
        var repairs = Enumerable.Range(0, count)
            .Select(i => new Repair { Id = (i + 1).ToString("x32"), Radius = radius * scale }).ToArray();
        for (var order = 0; order < 5; order++)
        {
            if (order > 0) new Random(order).Shuffle(repairs);
            var settings = new EditSettings { Repairs = repairs.ToList() };
            var loadError = Record.Exception(() =>
                EditSettingsJson.Deserialize(JsonSerializer.Serialize(settings), out _));
            var saveError = Record.Exception(() => EditSettingsJson.Serialize(settings));
            if (admitted)
            {
                Assert.Null(loadError);
                Assert.Null(saveError);
            }
            else
            {
                Assert.IsType<JsonException>(loadError);
                Assert.IsType<JsonException>(saveError);
            }
        }
    }

    [Fact]
    public void InvalidEntriesRejectOnBothBoundaries()
    {
        var valid = S64();
        RejectOnLoadAndSave(new() { Repairs = [.. valid, new Repair()] });
        foreach (var invalid in new Repair[] { new() { Id = "bad" }, new() { Id = null! },
                     new() { Type = "remove" }, new() { Type = null! }, null! })
            RejectOnLoadAndSave(new() { Repairs = [invalid] });
        RejectOnLoadAndSave(new() { Repairs = [valid[0], valid[0] with { Id = valid[0].Id.ToUpperInvariant() }] });
        foreach (var name in new[] { "U", "V", "Su", "Sv", "Radius", "Feather", "Opacity" })
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var repair = new Repair();
            typeof(Repair).GetProperty(name)!.SetValue(repair, value);
            Assert.Throws<JsonException>(() => EditSettingsJson.Serialize(new() { Repairs = [repair] }));
            var node = JsonNode.Parse(EditSettingsJson.Serialize(new() { Repairs = [new()] }))!;
            node["repairs"]![0]![name.ToLowerInvariant()] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.Throws<JsonException>(() => EditSettingsJson.Deserialize(node.ToJsonString(), out _));
        }
    }

    [Theory]
    [InlineData("id")]
    [InlineData("type")]
    [InlineData("u")]
    [InlineData("v")]
    [InlineData("su")]
    [InlineData("sv")]
    [InlineData("radius")]
    [InlineData("feather")]
    [InlineData("opacity")]
    public void MissingFieldsReject(string field)
    {
        var node = JsonNode.Parse(EditSettingsJson.Serialize(new() { Repairs = [new()] }))!;
        node["repairs"]![0]!.AsObject().Remove(field);
        Assert.Throws<JsonException>(() => EditSettingsJson.Deserialize(node.ToJsonString(), out _));
    }

    [Fact]
    public void ClampAndQuantizeWithoutMutatingTheSaveInput()
    {
        var settings = new EditSettings { Repairs = [new() { U = -1, V = 2, Su = .123456,
            Sv = .876543, Radius = -1, Feather = 2, Opacity = -1 }] };
        var raw = JsonSerializer.Serialize(settings);
        var loaded = EditSettingsJson.Deserialize(raw, out var changed);
        Assert.True(changed);
        var repair = Assert.Single(loaded.Repairs!);
        Assert.Equal(0, repair.U); Assert.Equal(1, repair.V);
        Assert.Equal(Math.Round(.123456 * 16384) / 16384, repair.Su);
        Assert.Equal(Math.Round(.876543 * 16384) / 16384, repair.Sv);
        Assert.Equal(.002, repair.Radius); Assert.Equal(1, repair.Feather); Assert.Equal(.05, repair.Opacity);
        Assert.Equal(EditSettingsJson.Serialize(loaded), EditSettingsJson.Serialize(settings));
        Assert.Equal(-1, settings.Repairs![0].U);
        repair.Radius = 10; repair.Feather = -1; repair.Opacity = 10;
        repair = Assert.Single(EditSettingsJson.Deserialize(JsonSerializer.Serialize(loaded), out changed).Repairs!);
        Assert.True(changed);
        Assert.Equal(.1, repair.Radius); Assert.Equal(0, repair.Feather); Assert.Equal(1, repair.Opacity);
    }

    [Fact]
    public void EveryRepairFieldChangesEqualityAndHashAndCloneIsIndependent()
    {
        var settings = new EditSettings { Repairs = [new()] };
        Assert.True(settings.HasEdits);
        var clone = settings.Clone();
        Assert.True(settings.HasSameEdits(clone));
        Assert.NotSame(settings.Repairs, clone.Repairs);
        Assert.NotSame(settings.Repairs![0], clone.Repairs![0]);
        foreach (var property in typeof(Repair).GetProperties())
        {
            clone = settings.Clone();
            object value = property.Name switch { "Id" => Guid.NewGuid().ToString("N"),
                "Type" => "clone", "Radius" => .02, "Opacity" => .5, _ => .25 };
            property.SetValue(clone.Repairs![0], value);
            Assert.False(settings.HasSameEdits(clone));
            Assert.NotEqual(RenderSettingsHash.Compute(settings), RenderSettingsHash.Compute(clone));
        }
        clone.Repairs!.Clear();
        Assert.Single(settings.Repairs);
        Assert.False(clone.HasEdits);
    }

    internal static List<Repair> S64() => HealWorkloads.S64().Select((s, i) => new Repair
    {
        Id = (i + 1).ToString("x32"), Type = s.IsClone ? "clone" : "heal",
        U = s.U, V = s.V, Su = s.Su, Sv = s.Sv, Radius = s.Radius,
        Feather = s.Feather, Opacity = s.Opacity
    }).ToList();

    private static void RejectOnLoadAndSave(EditSettings settings)
    {
        Assert.Throws<JsonException>(() => EditSettingsJson.Deserialize(JsonSerializer.Serialize(settings), out _));
        Assert.Throws<JsonException>(() => EditSettingsJson.Serialize(settings));
    }
}
