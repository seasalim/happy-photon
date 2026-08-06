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
            HlReconstruction = HlReconstructionMode.Clip,
            Detail = new DetailSettings { NoiseReduction = FbddMode.Light }
        };

        var json = JsonSerializer.Serialize(source);
        var result = JsonSerializer.Deserialize<EditSettings>(json);

        Assert.NotNull(result);
        Assert.Equal(HlReconstructionMode.Clip, result.HlReconstruction);
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
}
