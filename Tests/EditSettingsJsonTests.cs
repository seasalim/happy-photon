using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class EditSettingsJsonTests
{
    [Fact]
    public void Serialize_UsesCanonicalV2ShapeAndOrder()
    {
        var settings = new EditSettings
        {
            Exposure = 0.5,
            Wb = new WhiteBalanceSettings
            {
                Mode = WbMode.Custom,
                Kelvin = 6500,
                Tint = 10
            },
            Detail = new DetailSettings { NoiseReduction = FbddMode.Full }
        };
        settings.Crop = new CropRegion
        {
            Left = 0.1,
            Top = 0.2,
            Right = 0.8,
            Bottom = 0.9
        };

        var json = EditSettingsJson.Serialize(settings);
        using var document = JsonDocument.Parse(json);
        var names = document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("\"temperature\"", json);
        Assert.DoesNotContain("\"hasEdits\"", json);
        Assert.DoesNotContain("\"isIdentity\"", json);
        Assert.DoesNotContain("\"isFullImage\"", json);
        Assert.Contains("\"mode\":\"custom\"", json);
        Assert.Contains("\"noiseReduction\":\"full\"", json);
        Assert.Equal(
        [
            "version", "exposure", "wb", "highlights", "shadows",
            "brightness", "contrast", "saturation", "vibrance", "baseLook",
            "hlReconstruction", "detail", "rotation", "horizon_rotation",
            "crop", "curve", "applied_preset_id"
        ], names);
        Assert.Equal(
            ["mode", "kelvin", "tint", "gains", "preset"],
            document.RootElement.GetProperty("wb").EnumerateObject()
                .Select(property => property.Name));
        Assert.Equal(
            ["captureSharpen", "noiseReduction", "chromaNr"],
            document.RootElement.GetProperty("detail").EnumerateObject()
                .Select(property => property.Name));
    }

    [Fact]
    public void Serialize_WithoutChannelCurves_RemainsByteIdenticalToLegacyV2()
    {
        const string expected =
            "{\"version\":2,\"exposure\":0,\"wb\":{\"mode\":\"asShot\"," +
            "\"kelvin\":null,\"tint\":null,\"gains\":null,\"preset\":null}," +
            "\"highlights\":0,\"shadows\":0,\"brightness\":0,\"contrast\":0," +
            "\"saturation\":0,\"vibrance\":0,\"baseLook\":null," +
            "\"hlReconstruction\":\"clip\",\"detail\":{\"captureSharpen\":null," +
            "\"noiseReduction\":\"off\",\"chromaNr\":0},\"rotation\":0," +
            "\"horizon_rotation\":0,\"crop\":null,\"curve\":{\"points\":" +
            "[{\"x\":0,\"y\":0},{\"x\":1,\"y\":1}]}," +
            "\"applied_preset_id\":null}";

        Assert.Equal(expected, EditSettingsJson.Serialize(new EditSettings()));
    }

    [Fact]
    public void ChannelCurves_RoundTripOnlyWhenPresent()
    {
        var red = new CurveData();
        red.AddPointAndReturnIndex(0.5, 0.7);
        var source = new EditSettings { CurveRed = red };

        var json = EditSettingsJson.Serialize(source);
        var result = EditSettingsJson.Deserialize(json, out var wasClamped);

        Assert.False(wasClamped);
        Assert.Contains("\"curveRed\"", json);
        Assert.DoesNotContain("\"curveGreen\"", json);
        Assert.DoesNotContain("\"curveBlue\"", json);
        Assert.NotNull(result.CurveRed);
        Assert.Null(result.CurveGreen);
        Assert.Null(result.CurveBlue);
        Assert.True(result.CurveRed!.LookupTable[128] > 140);
    }

    [Fact]
    public void Deserialize_LegacyV2_DoesNotMaterializeOptionalChannels()
    {
        var json = EditSettingsJson.Serialize(new EditSettings());

        var result = EditSettingsJson.Deserialize(json, out _);

        Assert.Null(result.CurveRed);
        Assert.Null(result.CurveGreen);
        Assert.Null(result.CurveBlue);
        Assert.Equal(json, EditSettingsJson.Serialize(result));
    }

    [Fact]
    public void Effects_IdentityCanonicalizesToNullJsonAndHash()
    {
        var baseline = EditSettingsJson.Serialize(new EditSettings());
        var explicitIdentity = baseline.Replace(
            "\"rotation\":0",
            "\"effects\":{\"vignette\":0,\"midpoint\":87," +
            "\"grain\":0,\"grainSize\":\"coarse\"},\"rotation\":0",
            StringComparison.Ordinal);

        var settings = EditSettingsJson.Deserialize(
            explicitIdentity,
            out var wasClamped);

        Assert.False(wasClamped);
        Assert.Null(settings.Effects);
        Assert.Equal(baseline, EditSettingsJson.Serialize(settings));
        Assert.Equal(
            RenderSettingsHash.Compute(new EditSettings()),
            RenderSettingsHash.Compute(new EditSettings
            {
                Effects = new EffectsSettings
                {
                    Midpoint = 87,
                    GrainSize = GrainSize.Coarse
                }
            }));
    }

    [Fact]
    public void Effects_ActiveSettingsClampAndRoundTrip()
    {
        var json = EditSettingsJson.Serialize(new EditSettings
        {
            Effects = new EffectsSettings
            {
                Vignette = -150,
                Midpoint = 140,
                Grain = 120,
                GrainSize = GrainSize.Fine
            }
        });
        var settings = EditSettingsJson.Deserialize(json, out var wasClamped);

        Assert.False(wasClamped);
        Assert.NotNull(settings.Effects);
        Assert.Equal(-100, settings.Effects!.Vignette);
        Assert.Equal(100, settings.Effects.Midpoint);
        Assert.Equal(100, settings.Effects.Grain);
        Assert.Equal(GrainSize.Fine, settings.Effects.GrainSize);
        Assert.Contains("\"grainSize\":\"fine\"", json);
    }

    [Fact]
    public void Mixer_IdentityCanonicalizesToNullLegacyJsonAndHash()
    {
        var identity = new EditSettings
        {
            Mixer = new ColorMixerSettings()
        };
        var legacy = EditSettingsJson.Serialize(new EditSettings());

        var json = EditSettingsJson.Serialize(identity);
        var roundTrip = EditSettingsJson.Deserialize(json, out var wasClamped);

        Assert.False(wasClamped);
        Assert.Equal(legacy, json);
        Assert.Null(roundTrip.Mixer);
        Assert.False(identity.HasEdits);
        Assert.Equal(
            RenderSettingsHash.Compute(new EditSettings()),
            RenderSettingsHash.Compute(identity));
    }

    [Fact]
    public void Mixer_ActiveSettingsClampRoundTripAndAffectHash()
    {
        var source = new EditSettings { Mixer = new ColorMixerSettings() };
        source.Mixer.Red.Hue = 150;
        source.Mixer.Orange.Saturation = -150;
        source.Mixer.Magenta.Luminance = 47;

        var json = EditSettingsJson.Serialize(source);
        var settings = EditSettingsJson.Deserialize(json, out var wasClamped);

        Assert.False(wasClamped);
        Assert.Contains("\"mixer\"", json);
        Assert.Equal(100, settings.Mixer!.Red.Hue);
        Assert.Equal(-100, settings.Mixer.Orange.Saturation);
        Assert.Equal(47, settings.Mixer.Magenta.Luminance);
        Assert.Equal(150, source.Mixer.Red.Hue);
        Assert.True(settings.HasEdits);
        Assert.NotEqual(
            RenderSettingsHash.Compute(new EditSettings()),
            RenderSettingsHash.Compute(settings));

        var outOfRange = json.Replace(
            "\"luminance\":47",
            "\"luminance\":147",
            StringComparison.Ordinal);
        var clamped = EditSettingsJson.Deserialize(outOfRange, out wasClamped);
        Assert.True(wasClamped);
        Assert.Equal(100, clamped.Mixer!.Magenta.Luminance);
    }

    [Fact]
    public void Deserialize_ClampsAllPersistedSliderRanges()
    {
        var json = """
            {
              "version": 2,
              "exposure": 9,
              "wb": { "mode": "custom", "kelvin": 18000, "tint": -300,
                      "gains": [0.1, 1, 9], "preset": null },
              "highlights": 150, "shadows": -150,
              "brightness": 200, "contrast": -200,
              "saturation": 101, "vibrance": -101,
              "baseLook": null, "hlReconstruction": "blend",
              "detail": { "captureSharpen": 150, "noiseReduction": "off",
                          "chromaNr": -1 },
              "effects": { "vignette": -150, "midpoint": 150,
                           "grain": 150, "grainSize": "coarse" },
              "rotation": 0, "horizon_rotation": 8,
              "crop": null, "curve": { "points": [{"x":0,"y":0},{"x":1,"y":1}] },
              "applied_preset_id": null
            }
            """;

        var settings = EditSettingsJson.Deserialize(json, out var wasClamped);

        Assert.True(wasClamped);
        Assert.Equal(3, settings.Exposure);
        Assert.Equal(12000, settings.Wb.Kelvin);
        Assert.Equal(-100, settings.Wb.Tint);
        Assert.Equal([0.2, 1, 5], settings.Wb.Gains!);
        Assert.Equal(100, settings.Highlights);
        Assert.Equal(-100, settings.Shadows);
        Assert.Equal(100, settings.Brightness);
        Assert.Equal(-100, settings.Contrast);
        Assert.Equal(100, settings.Detail.CaptureSharpen);
        Assert.Equal(0, settings.Detail.ChromaNr);
        Assert.Equal(-100, settings.Effects!.Vignette);
        Assert.Equal(100, settings.Effects.Midpoint);
        Assert.Equal(100, settings.Effects.Grain);
        Assert.Equal(5, settings.HorizonRotation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(99)]
    public void Serialize_RejectsUnsupportedVersions(int version)
    {
        Assert.Throws<NotSupportedException>(() =>
            EditSettingsJson.Serialize(new EditSettings { Version = version }));
    }

    [Fact]
    public void Serialize_ClampsCloneWithoutMutatingSource()
    {
        var source = new EditSettings { Exposure = 9 };

        var json = EditSettingsJson.Serialize(source);
        var serialized = EditSettingsJson.Deserialize(json, out _);

        Assert.Equal(9, source.Exposure);
        Assert.Equal(3, serialized.Exposure);
    }

    [Fact]
    public void CurrentDocument_RoundTripsThroughDefaultJsonSerializer()
    {
        var source = new EditSettings
        {
            HlReconstruction = HlReconstructionMode.Blend,
            Detail = new DetailSettings { NoiseReduction = FbddMode.Light }
        };

        var json = JsonSerializer.Serialize(source);
        var result = JsonSerializer.Deserialize<EditSettings>(json);

        Assert.NotNull(result);
        Assert.Equal(HlReconstructionMode.Blend, result.HlReconstruction);
        Assert.Equal(FbddMode.Light, result.Detail.NoiseReduction);
    }

    [Fact]
    public void CurrentDocument_RejectsNumericEnumEncoding()
    {
        var json = EditSettingsJson.Serialize(new EditSettings())
            .Replace(
                "\"hlReconstruction\":\"clip\"",
                "\"hlReconstruction\":0",
                StringComparison.Ordinal);

        Assert.Throws<JsonException>(() =>
            EditSettingsJson.Deserialize(json, out _));
    }

    [Fact]
    public void Effects_RejectUnknownGrainSize()
    {
        var json = EditSettingsJson.Serialize(new EditSettings
        {
            Effects = new EffectsSettings { Grain = 10 }
        }).Replace("\"medium\"", "\"huge\"", StringComparison.Ordinal);

        Assert.Throws<JsonException>(() =>
            EditSettingsJson.Deserialize(json, out _));
        Assert.Throws<JsonException>(() =>
            EditSettingsJson.Serialize(new EditSettings
            {
                Effects = new EffectsSettings
                {
                    Grain = 10,
                    GrainSize = (GrainSize)99
                }
            }));
    }

    [Theory]
    [InlineData("""{"mode":"custom","kelvin":null,"tint":0,"gains":null,"preset":null}""")]
    [InlineData("""{"mode":"preset","kelvin":5500,"tint":0,"gains":null,"preset":null}""")]
    public void CurrentDocument_RejectsMissingModeSpecificWhiteBalanceValues(string wb)
    {
        var json = EditSettingsJson.Serialize(new EditSettings())
            .Replace(
                """{"mode":"asShot","kelvin":null,"tint":null,"gains":null,"preset":null}""",
                wb,
                StringComparison.Ordinal);

        Assert.Throws<JsonException>(() =>
            EditSettingsJson.Deserialize(json, out _));
    }

    [Fact]
    public void CurrentDocument_RejectsRemovedManualWhiteBalanceMode()
    {
        var json = EditSettingsJson.Serialize(new EditSettings())
            .Replace("\"asShot\"", "\"manual\"", StringComparison.Ordinal);

        Assert.Throws<JsonException>(() =>
            EditSettingsJson.Deserialize(json, out _));
    }

    [Fact]
    public void RawProfile_RoundTripsAsAdditiveV2Field()
    {
        var settings = new EditSettings
        {
            RawProfile = new RawProfileSelection
            {
                Source = RawProfileSource.UserFile,
                Location = "C:/profiles/synthetic.dcp",
                ContentHash = new string('b', 64)
            }
        };

        var json = EditSettingsJson.Serialize(settings);
        var result = EditSettingsJson.Deserialize(json, out var wasClamped);

        Assert.False(wasClamped);
        Assert.Contains("\"rawProfile\"", json);
        Assert.Equal(RawProfileSource.UserFile, result.RawProfile?.Source);
        Assert.Equal(settings.RawProfile.Location, result.RawProfile?.Location);
        Assert.Equal(settings.RawProfile.ContentHash, result.RawProfile?.ContentHash);
        Assert.True(result.HasEdits);
    }

    [Fact]
    public void BuiltInProfile_OmitsFieldAndPreservesLegacyCanonicalJson()
    {
        var settings = new EditSettings();
        var json = EditSettingsJson.Serialize(settings);

        Assert.DoesNotContain("rawProfile", json);
        Assert.Null(EditSettingsJson.Deserialize(json, out _).RawProfile);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void RawProfile_RejectsInvalidContentHash(string hash)
    {
        var settings = new EditSettings
        {
            RawProfile = new RawProfileSelection
            {
                Source = RawProfileSource.Embedded,
                ContentHash = hash
            }
        };

        Assert.Throws<JsonException>(() => EditSettingsJson.Serialize(settings));
    }
}
