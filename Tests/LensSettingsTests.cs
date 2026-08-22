using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LensSettingsTests
{
    [Fact]
    public void NewAndLegacyDocumentsKeepTheirOwnBaseline()
    {
        var current = new EditSettings();
        Assert.Equal(LensBaseline.Standard, current.Lens.Baseline);
        Assert.True(current.Lens.Distortion);
        Assert.True(current.Lens.ChromaticAberration);
        Assert.False(current.HasEdits);

        var legacyJson = EditSettingsJson.Serialize(current)
            .Replace("\"version\":3", "\"version\":2", StringComparison.Ordinal)
            .Replace(",\"lens\":{\"distortion\":true,\"chromaticAberration\":true," +
                "\"vignetting\":false,\"baseline\":\"standard\"}", "", StringComparison.Ordinal);
        var legacy = EditSettingsJson.Deserialize(legacyJson, out _);

        Assert.Equal(EditSettings.CurrentVersion, legacy.Version);
        Assert.Equal(LensBaseline.Legacy, legacy.Lens.Baseline);
        Assert.False(legacy.Lens.Distortion);
        Assert.False(legacy.Lens.ChromaticAberration);
        Assert.False(legacy.Lens.Vignetting);
        Assert.False(legacy.HasEdits);

        var roundTrip = EditSettingsJson.Deserialize(
            EditSettingsJson.Serialize(legacy), out _);
        Assert.Equal(LensBaseline.Legacy, roundTrip.Lens.Baseline);
        Assert.False(roundTrip.HasEdits);
    }

    [Fact]
    public void TransferCopiesTogglesButPreservesTargetBaseline()
    {
        var source = new EditSettings();
        source.Lens.Distortion = false;
        source.Lens.Vignetting = true;
        var target = new EditSettings { Lens = LensSettings.Legacy() };

        EditSettingsTransfer.ApplySubset(
            EditSettingsTransfer.CopySubset(source), target);

        Assert.Equal(LensBaseline.Legacy, target.Lens.Baseline);
        Assert.False(target.Lens.Distortion);
        Assert.True(target.Lens.ChromaticAberration);
        Assert.True(target.Lens.Vignetting);
        Assert.True(target.HasEdits);
    }

    [Fact]
    public void ToggleBitsJoinDecodeIdentity()
    {
        var standard = BaseDecodeSettings.From(new EditSettings());
        var legacy = BaseDecodeSettings.From(new EditSettings
        {
            Lens = LensSettings.Legacy()
        });
        var vignetting = BaseDecodeSettings.From(new EditSettings
        {
            Lens = new LensSettings { Vignetting = true }
        });

        Assert.EndsWith("lens=110", standard.CacheKey, StringComparison.Ordinal);
        Assert.EndsWith("lens=000", legacy.CacheKey, StringComparison.Ordinal);
        Assert.EndsWith("lens=111", vignetting.CacheKey, StringComparison.Ordinal);
        Assert.Equal(3, new[] { standard.CacheKey, legacy.CacheKey, vignetting.CacheKey }
            .Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void VersionThreeRequiresExplicitBaselineMarker()
    {
        Assert.Throws<JsonException>(() =>
            EditSettingsJson.Deserialize("""{"version":3}""", out _));
        Assert.Throws<JsonException>(() =>
            EditSettingsJson.Deserialize(
                """{"version":3,"lens":{"distortion":true}}""", out _));
    }
}
