using System.Text.Json;
using System.Text.Json.Nodes;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class FinishingLookTests
{
    [Fact]
    public void EveryTransferLookFieldHasAnExplicitClassification()
    {
        string[] corrections =
        [
            "wb", "exposure", "brightness", "shadows", "highlights", "whites", "blacks",
            "baseLook", "hlReconstruction", "detail", "lens.distortion",
            "lens.chromaticAberration", "lens.vignetting"
        ];
        var transfer = EditSettingsTransfer.LookGroups.SelectMany(group => group.Fields).Order().ToArray();
        var classified = FinishingLookHarness.LookFields.Concat(corrections).Order().ToArray();
        Assert.Equal(classified.Length, classified.Distinct().Count());
        Assert.Equal(transfer, classified);
    }

    [Fact]
    public void CandidateFilesContainOnlyLookSettings()
    {
        var candidates = FinishingLookHarness.Candidates();
        Assert.InRange(candidates.Length, 25, 30);
        Assert.Equal(candidates.Length, candidates.Select(candidate => candidate.Id).Distinct().Count());
        Assert.Equal(candidates.Length, candidates.Select(candidate => candidate.Name).Distinct().Count());
        Assert.Equal(new[] { "Black & White", "Creative", "Landscape", "Natural", "Portrait" },
            candidates.Select(candidate => candidate.Group).Distinct().Order());

        foreach (var group in candidates.GroupBy(candidate => candidate.Group))
        {
            Assert.InRange(group.Count(), 5, 6);
            Assert.Equal(Enumerable.Range(1, group.Count()), group.Select(candidate => candidate.Order).Order());
        }

        foreach (var path in Directory.GetFiles(FinishingLookHarness.Folder, "*.preset.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var candidate = JsonSerializer.Deserialize<FinishingCandidate>(document)!;
            Assert.StartsWith("builtin_", candidate.Id);
            Assert.Equal(candidate.Id + ".preset.json", Path.GetFileName(path));
            Assert.Equal(UserPresetFile.CurrentVersion, candidate.Version);
            Assert.False(string.IsNullOrWhiteSpace(candidate.Intent));
            var loaded = FinishingLookHarness.Load(path);
            Assert.True(candidate.Settings.Clone().HasSameEdits(loaded.Settings));

            foreach (var field in document.RootElement.GetProperty("settings").EnumerateObject())
            {
                if (field.Name == "version")
                {
                    Assert.Equal(EditSettings.CurrentVersion, field.Value.GetInt32());
                    continue;
                }

                Assert.Contains(field.Name, FinishingLookHarness.LookFields);
            }

            if (candidate.Group == "Black & White")
            {
                Assert.Equal(-100, candidate.Settings.Saturation);
                Assert.NotNull(candidate.Settings.Mixer);

                foreach (var band in Enum.GetValues<ColorMixerBand>())
                {
                    Assert.Equal(0, candidate.Settings.Mixer.GetBand(band).Hue);
                    Assert.Equal(0, candidate.Settings.Mixer.GetBand(band).Saturation);
                }
            }
        }
    }

    [Fact]
    public void CandidatesPreserveCorrectionsReplaceEachOtherAndRemoveExactly()
    {
        var candidates = FinishingLookHarness.Candidates();
        var corpus = SyncTransferParityCorpus.Sources().Concat(SyncTransferParityCorpus.Destinations());

        foreach (var item in corpus)
        {
            var original = JsonSerializer.SerializeToUtf8Bytes(item.Settings);
            var corrections = FinishingLookHarness.CorrectionBytes(item.Settings);

            foreach (var first in candidates)
            {
                var applied = FinishingLookHarness.Apply(item.Settings, first.Settings);
                AssertLookFields(first.Settings, applied);
                AssertLookFields(new(), FinishingLookHarness.Remove(applied));
                Assert.Equal(corrections, FinishingLookHarness.CorrectionBytes(applied));
                Assert.Equal(corrections, FinishingLookHarness.CorrectionBytes(FinishingLookHarness.Remove(applied)));
                Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(FinishingLookHarness.Remove(item.Settings)),
                    JsonSerializer.SerializeToUtf8Bytes(FinishingLookHarness.Remove(applied)));

                foreach (var second in candidates)
                {
                    Assert.Equal(
                        JsonSerializer.SerializeToUtf8Bytes(FinishingLookHarness.Apply(item.Settings, second.Settings)),
                        JsonSerializer.SerializeToUtf8Bytes(FinishingLookHarness.Apply(applied, second.Settings)));
                }
            }

            Assert.Equal(original, JsonSerializer.SerializeToUtf8Bytes(item.Settings));
        }
    }

    private static void AssertLookFields(EditSettings expected, EditSettings actual)
    {
        var expectedJson = JsonSerializer.SerializeToNode(expected)!;
        var actualJson = JsonSerializer.SerializeToNode(actual)!;

        foreach (var field in FinishingLookHarness.LookFields)
        {
            Assert.True(JsonNode.DeepEquals(expectedJson[field], actualJson[field]), field);
        }
    }

    [Fact]
    public void FrozenManifestMatchesSettingsWithoutConsultingResults()
    {
        var path = Path.Combine(FinishingLookHarness.Folder, "exempt-components.json");
        var frozen = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(path))!;
        var candidates = FinishingLookHarness.Candidates();
        Assert.Equal(candidates.Select(candidate => candidate.Id).Order(), frozen.Keys.Order());

        foreach (var candidate in candidates)
        {
            Assert.Equal(FinishingLookManifest.Derive(candidate.Settings), frozen[candidate.Id]);
        }
    }

    [Fact]
    public void ExemptionFindsCurveOvershootBetweenNonBrighteningKnots()
    {
        var curve = new CurveData
        {
            Points = [new(0, 0), new(.1, .1), new(.8, .8), new(1, 1)]
        };
        Assert.True(FinishingLookManifest.AboveIdentity(curve));
        Assert.False(FinishingLookManifest.AboveIdentity(null));
        Assert.False(FinishingLookManifest.AboveIdentity(new CurveData()));
    }
}

