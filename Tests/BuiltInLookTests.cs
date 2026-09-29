using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BuiltInLookTests
{
    [Fact]
    public async Task StartupAndPersonalLookupDoNotReadBuiltIns()
    {
        using var directory = new TemporaryDirectory();
        var service = new PresetService(directory.Path);
        await service.InitializeAsync();
        Assert.Empty(service.UserPresets);
        Assert.Null(service.GetById("user_missing"));
        Assert.True(service.ContainsId("builtin_fresh_start"));
        Assert.False(service.ContainsId("builtin_unknown"));
        Assert.False(service.AreBuiltInsLoaded);
        Assert.Equal(26, service.BuiltInPresets.Count);
        Assert.True(service.AreBuiltInsLoaded);
    }

    [Fact]
    public void EmbeddedBytesMatchApprovedParentAndManifestOrder()
    {
        var hashes = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(
            Path.Combine(FinishingLookHarness.Folder, "approved-sha256.json")))!;
        var service = new PresetService();
        using var manifest = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(FinishingLookHarness.ShippedFolder, "approved.json")));
        var groups = manifest.RootElement.GetProperty("groups").EnumerateArray().ToArray();
        Assert.Equal(groups.SelectMany(group => group.GetProperty("looks").EnumerateArray())
            .Select(id => id.GetString()), service.BuiltInPresets.Select(preset => preset.Id));
        Assert.Equal(new[] { "Natural", "Portrait", "Landscape", "Black & White", "Creative" },
            service.BuiltInPresets.Select(preset => preset.Group).Distinct());
        Assert.Null(service.GetById("builtin_living_color"));

        foreach (var (name, hash) in hashes)
        {
            using var stream = typeof(PresetService).Assembly.GetManifestResourceStream(
                $"HappyPhoton.Assets.Looks.{name}");
            Assert.NotNull(stream);
            Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(stream)));
            Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(
                Path.Combine(FinishingLookHarness.ShippedFolder, name)))));
        }

        foreach (var look in service.BuiltInPresets)
        {
            Assert.True(look.IsBuiltIn);
            Assert.False(string.IsNullOrWhiteSpace(look.Description));
            var candidate = FinishingLookHarness.Load(FinishingLookHarness.CandidatePath(look.Id));
            Assert.Equal(candidate.Group, look.Group);
            Assert.Equal(candidate.Order, look.Order);
            Assert.Equal(candidate.Intent, look.Description);
        }
    }

    [Fact]
    public void ProductionTransferMatchesApprovedHarnessAndPreservesEveryCorrection()
    {
        var looks = new PresetService().BuiltInPresets;
        var settings = SyncTransferParityCorpus.Sources().Concat(SyncTransferParityCorpus.Destinations())
            .Select(item => item.Settings).Append(new EditSettings());

        foreach (var source in settings)
        {
            foreach (var look in looks)
            {
                var candidate = FinishingLookHarness.Load(FinishingLookHarness.CandidatePath(look.Id));
                var expected = FinishingLookHarness.Apply(source, candidate.Settings);
                var actual = source.Clone();
                EditSettingsLook.Apply(look.Settings, actual);
                Assert.Equal(EditSettingsJson.Serialize(expected), EditSettingsJson.Serialize(actual));
                Assert.Equal(FinishingLookHarness.CorrectionBytes(source), FinishingLookHarness.CorrectionBytes(actual));

                foreach (var next in looks)
                {
                    EditSettingsLook.Apply(next.Settings, actual);
                    Assert.Equal(EditSettingsJson.Serialize(FinishingLookHarness.Apply(source, next.Settings)),
                        EditSettingsJson.Serialize(actual));
                }

                EditSettingsLook.Reset(actual);
                Assert.Equal(EditSettingsJson.Serialize(FinishingLookHarness.Remove(source)),
                    EditSettingsJson.Serialize(actual));
            }
        }
    }

    [Fact]
    public void SparseReaderRejectsCorrectionsAndMissingIntentAndClampsLookValues()
    {
        var node = JsonNode.Parse(File.ReadAllText(FinishingLookHarness.CandidatePaths.First()))!;
        node["settings"]!["contrast"] = 200;
        Assert.Equal(100, Read(node).Settings.Contrast);
        node["settings"]!["exposure"] = 0;
        Assert.Throws<JsonException>(() => Read(node));
        node["settings"]!.AsObject().Remove("exposure");
        node["intent"] = " ";
        Assert.Throws<JsonException>(() => Read(node));
        node.AsObject().Remove("intent");
        Assert.ThrowsAny<Exception>(() => Read(node));
    }

    [Fact]
    public async Task BuiltInsCannotBeMutatedAndSaveCurrentKeepsPersonalSubset()
    {
        using var directory = new TemporaryDirectory();
        var service = new PresetService(directory.Path);
        await service.InitializeAsync();
        var look = service.BuiltInPresets[0];
        await service.RenameUserPresetAsync(look.Id, "Changed");
        await service.DeleteUserPresetAsync(look.Id);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveUserPresetAsync("Changed", new(), look.Id));
        Assert.Same(look, service.GetById(look.Id));
        Assert.Empty(Directory.GetFiles(directory.Path));
        var source = SyncTransferParityCorpus.CreateLook();
        EditSettingsLook.Apply(look.Settings, source);
        var saved = await service.SaveUserPresetAsync("Personal", source);
        var expected = EditSettingsTransfer.CopyGroups(source, EditSettingsTransfer.LookGroups);
        expected.AppliedPresetId = null;
        Assert.False(saved.IsBuiltIn);
        Assert.Equal(EditSettingsJson.Serialize(expected), EditSettingsJson.Serialize(saved.Settings));
    }

    [Fact]
    public async Task EveryGroupPreferenceSurvivesCatalogRestart()
    {
        using var directory = new TemporaryDirectory();
        var expected = new PresetService().BuiltInPresets.Select(look => look.Group!).Distinct()
            .Prepend("My Presets").ToDictionary(group => group, _ => false);

        using (var catalog = new CatalogService(directory.Path))
        {
            await catalog.InitializeAsync();
            await new AppSettingsService(catalog).SavePreferencesAsync(new() { PresetGroups = expected });
        }

        using var reopened = new CatalogService(directory.Path);
        await reopened.InitializeAsync();
        var restored = await new AppSettingsService(reopened).LoadAsync();
        Assert.Equal(expected.OrderBy(pair => pair.Key), restored.PresetGroups.OrderBy(pair => pair.Key));
    }

    private static Preset Read(JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());

        return BuiltInLookLibrary.Read(document.RootElement);
    }
}
