using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using HappyPhoton.Models;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class EditSettingsGroupTests
{
    [Fact]
    public void RegistryAssignsEverySerializedFieldExactlyOnce()
    {
        AssertCoverage(SerializedFields());
        Assert.Equal(EditSettingsTransfer.LookGroups, EditSettingsTransfer.DefaultGroups);
        Assert.Equal(8, EditSettingsTransfer.LookGroups.Count);
        Assert.All(EditSettingsTransfer.Groups.Where(group => !group.IsDefault),
            group => Assert.Equal(EditSettingsGroupKind.PhotoSpecific, group.Kind));
    }

    [Theory]
    [InlineData("futureField")]
    [InlineData("lens.futureField")]
    public void CoverageRejectsAnUnassignedField(string field)
    {
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertCoverage([.. SerializedFields(), field]));
    }

    [Fact]
    public void EachGroupReplacesOnlyItsFieldsAcrossTheCorpus()
    {
        foreach (var group in EditSettingsTransfer.Groups)
        {
            foreach (var source in SyncTransferParityCorpus.Sources())
            {
                var target = SyncTransferParityCorpus.CreateLook();
                target.Rotation = 270;
                target.AppliedPresetId = "target-preset";
                var before = JsonSerializer.SerializeToNode(target)!;
                var sourceJson = JsonSerializer.SerializeToNode(source.Settings)!;
                EditSettingsTransfer.ApplyGroups(source.Settings, target, [group]);
                var after = JsonSerializer.SerializeToNode(target)!;

                foreach (var field in SerializedFields().Where(field => field != "applied_preset_id"))
                {
                    var expected = Field(group.Fields.Contains(field) ? sourceJson : before, field);
                    Assert.True(JsonNode.DeepEquals(expected, Field(after, field)),
                        $"{group.Name}/{source.Name}/{field}");
                }

                Assert.Null(target.AppliedPresetId);
            }
        }
    }

    [Fact]
    public void MarkerRequiresEveryLookGroup()
    {
        var source = new EditSettings { AppliedPresetId = "source-preset" };
        var target = new EditSettings { AppliedPresetId = "target-preset" };
        EditSettingsTransfer.ApplyGroups(source, target, EditSettingsTransfer.Groups);
        Assert.Equal(source.AppliedPresetId, target.AppliedPresetId);

        foreach (var omitted in EditSettingsTransfer.LookGroups)
        {
            EditSettingsTransfer.ApplyGroups(source, target,
                EditSettingsTransfer.Groups.Where(group => group != omitted).ToArray());
            Assert.Null(target.AppliedPresetId);
        }

        EditSettingsTransfer.ApplyGroups(source, target, []);
        Assert.Null(target.AppliedPresetId);
    }

    [Fact]
    public void AllGroupsIsolateMutableValuesAndReplaceLists()
    {
        var source = SyncTransferParityCorpus.CreateLook();
        source.Wb.Gains = [1.1, 1, .9];
        source.Crop = new CropRegion { Left = .1 };
        source.Geometry = new GeometrySettings { Vertical = 23 };
        source.RawProfile = new RawProfileSelection { Location = "profile.dcp" };
        source.Locals = [new LocalAdjustment { Id = "source-local" }];
        source.Repairs = [new Repair { Id = "source-repair" }];
        var target = new EditSettings
        {
            Locals = [new LocalAdjustment { Id = "target-local" }],
            Repairs = [new Repair { Id = "target-repair" }]
        };

        var before = JsonSerializer.Serialize(source);
        EditSettingsTransfer.ApplyGroups(source, target, EditSettingsTransfer.Groups);
        Assert.Equal("source-local", Assert.Single(target.Locals!).Id);
        Assert.Equal("source-repair", Assert.Single(target.Repairs!).Id);
        Assert.NotSame(source.Curve, target.Curve);
        Assert.NotSame(source.CurveRed, target.CurveRed);
        Assert.NotSame(source.CurveGreen, target.CurveGreen);
        Assert.NotSame(source.CurveBlue, target.CurveBlue);
        Assert.NotSame(source.Mixer, target.Mixer);
        Assert.NotSame(source.Lens, target.Lens);

        target.Wb.Gains![0] = 9;
        target.Detail.ChromaNr = 89;
        target.Effects!.Grain = 67;
        target.Crop!.Left = .2;
        target.Geometry!.Vertical = -23;
        target.RawProfile!.Location = "changed.dcp";
        target.Locals![0].Exposure = 2;
        target.Repairs![0].Radius = .08;

        Assert.Equal(before, JsonSerializer.Serialize(source));
    }

    private static JsonNode? Field(JsonNode document, string path) =>
        path.Split('.').Aggregate((JsonNode?)document, (node, part) => node?[part]);

    private static IEnumerable<string> SerializedFields() =>
        Properties(typeof(EditSettings)).SelectMany(name => name == "lens"
            ? Properties(typeof(LensSettings)).Select(child => $"lens.{child}")
            : [name]);

    private static IEnumerable<string> Properties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition != JsonIgnoreCondition.Always)
            .Select(property => property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name);

    private static void AssertCoverage(IEnumerable<string> fields)
    {
        var assigned = EditSettingsTransfer.Groups.SelectMany(group => group.Fields)
            .Concat(EditSettingsTransfer.NeverTransfers).ToArray();
        Assert.Equal(assigned.Length, assigned.Distinct().Count());
        Assert.Equal(fields.Order(), assigned.Order());
    }
}
