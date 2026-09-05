using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LensSettingsTests
{
    [Fact]
    public void ResetRestoresLensDefaults()
    {
        var current = new EditSettings();
        Assert.True(current.Lens.Distortion);
        Assert.True(current.Lens.ChromaticAberration);
        Assert.False(current.HasEdits);

        current.Lens.Distortion = false;
        current.Lens.ChromaticAberration = false;
        current.Lens.Vignetting = true;
        Assert.True(current.HasEdits);
        current.Lens.RestoreBaseline();
        Assert.True(current.Lens.Distortion);
        Assert.True(current.Lens.ChromaticAberration);
        Assert.False(current.Lens.Vignetting);
        Assert.False(current.HasEdits);
    }

    [Fact]
    public void ToggleBitsJoinDecodeIdentity()
    {
        var standard = BaseDecodeSettings.From(new EditSettings());
        var disabled = BaseDecodeSettings.From(new EditSettings
        {
            Lens = new LensSettings { Distortion = false, ChromaticAberration = false }
        });
        var vignetting = BaseDecodeSettings.From(new EditSettings
        {
            Lens = new LensSettings { Vignetting = true }
        });

        Assert.EndsWith("lens=110", standard.CacheKey, StringComparison.Ordinal);
        Assert.EndsWith("lens=000", disabled.CacheKey, StringComparison.Ordinal);
        Assert.EndsWith("lens=111", vignetting.CacheKey, StringComparison.Ordinal);
        Assert.Equal(3, new[] { standard.CacheKey, disabled.CacheKey, vignetting.CacheKey }
            .Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void VersionThreeRequiresAllLensBooleans()
    {
        Assert.Throws<JsonException>(() =>
            EditSettingsJson.Deserialize("""{"version":3}""", out _));
        Assert.Throws<JsonException>(() =>
            EditSettingsJson.Deserialize(
                """{"version":3,"lens":{"distortion":true}}""", out _));
    }
}
