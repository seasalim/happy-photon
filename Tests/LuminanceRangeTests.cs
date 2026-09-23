using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LuminanceRangeTests
{
    [Fact]
    public void MissingEnabledInHandEditedRangeDefaultsOff()
    {
        var settings = new EditSettings { Locals = [new() { Luminance = new() { Enabled = true, Lower = .47 } }] };
        var node = System.Text.Json.Nodes.JsonNode.Parse(EditSettingsJson.Serialize(settings))!;
        node["locals"]![0]!["luminance"] = new System.Text.Json.Nodes.JsonObject();
        var loaded = EditSettingsJson.Deserialize(node.ToJsonString(), out _);
        Assert.Equal(new LuminanceRange(), loaded.Locals![0].Luminance);
        Assert.False(loaded.Locals[0].Luminance!.Enabled);
        node["locals"]![0]!["luminance"]!["lower"] = .47;
        loaded = EditSettingsJson.Deserialize(node.ToJsonString(), out _);
        Assert.False(loaded.Locals![0].Luminance!.IsEffective);
    }

    [Fact]
    public void WindowsAndClassifierMatchIndependentOracle()
    {
        foreach (var lower in new[] { 0, .2, .5, 1 })
        foreach (var upper in new[] { lower, 1 })
        foreach (var softness in new[] { 0, .1, .5 })
        {
            var range = new LuminanceRange { Enabled = true, Lower = lower, Upper = upper, Softness = softness };
            var oracle = new LocalsRangeOracle.LightWindow(lower, upper, softness);
            for (var i = -100; i <= 200; i++)
                Assert.Equal(oracle.Weight(i / 100d), LuminanceWindow.Weight(range, i / 100d));
        }
        foreach (var r in new[] { -.2, 0, .18, 1, 2 })
        foreach (var g in new[] { -.1, 0, .3, 1, 4 })
        foreach (var b in new[] { -.3, 0, .5, 1, 3 })
            Assert.InRange(Math.Abs(LocalsRangeOracle.ClassifyLightness(r, g, b) - OklabColor.ClassifyLightness(r, g, b)), 0, 1e-14);
    }

    [Fact]
    public void PersistenceClampsRejectsAndCopiesWithoutChangingAbsentBytes()
    {
        var settings = new EditSettings { Locals = [new()] };
        var absent = EditSettingsJson.Serialize(settings);
        Assert.DoesNotContain("\"luminance\":", absent);
        var hash = RenderSettingsHash.Compute(settings);
        settings.Locals[0].Luminance = new() { Enabled = false, Lower = .47 };
        var saved = EditSettingsJson.Serialize(settings);
        Assert.NotEqual(hash, RenderSettingsHash.Compute(settings));
        var copy = EditSettingsJson.Deserialize(saved, out var clamped);
        Assert.False(clamped); Assert.Equal(saved, EditSettingsJson.Serialize(copy));
        var clone = copy.Clone();
        clone.Locals![0].Luminance = clone.Locals[0].Luminance! with { Lower = .6 };
        Assert.Equal(.47, copy.Locals![0].Luminance!.Lower);
        Assert.False(copy.HasSameEdits(clone));
        EditSettingsTransfer.ApplySubset(new() { Exposure = 2 }, copy);
        Assert.Equal(.47, copy.Locals[0].Luminance!.Lower);
        Assert.Null(EditSettingsTransfer.CopySubset(copy).Locals);
        settings.Locals[0].Luminance = new() { Enabled = true, Lower = 2, Upper = -1, Softness = 2 };
        var bounded = EditSettingsJson.Deserialize(JsonSerializer.Serialize(settings), out clamped).Locals![0].Luminance!;
        Assert.True(clamped); Assert.Equal((0d, 0d, .5), (bounded.Lower, bounded.Upper, bounded.Softness));
        foreach (var property in new[] { "lower", "upper", "softness" })
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(saved)!;
            node["locals"]![0]!["luminance"]![property] = 12345;
            Assert.Throws<JsonException>(() => EditSettingsJson.Deserialize(node.ToJsonString().Replace("12345", "1e999"), out _));
        }
        settings.Locals[0].Luminance = new() { Enabled = true, Lower = double.NaN };
        Assert.Throws<JsonException>(() => EditSettingsJson.Serialize(settings));
        settings.Locals[0].Luminance = null;
        Assert.Equal(absent, EditSettingsJson.Serialize(settings));
    }

    [Fact]
    public void EffectiveRangeUsesUnnormalizedBasisWithoutAdjustmentFeedback()
    {
        using var basis = RenderPipelineTestSupport.CreateBase([5000, 10000, 30000]);
        var local = new LocalAdjustment { Exposure = 1, Cu = 2, Angle = 0, Luminance = new() { Enabled = true, Lower = .5, Upper = .6 } };
        var settings = new EditSettings { Locals = [local, local with { Id = Guid.NewGuid().ToString("N"), Ordinal = 2 }] };
        using var geometry = RenderGeometry.Apply(basis.Pixels, settings, out var trace);
        var fused = RenderLocals.Create(settings, trace, 1, 1, info: basis.Info)!;
        double r = .1, g = .1, b = .1;
        var l = LocalsRangeOracle.ClassifyLightness(.2, .2, .2);
        var weight = new LocalsRangeOracle.LightWindow(.5, .6, .1).Weight(l);
        fused.ApplyColor(0, ref r, ref g, ref b, 2);
        Assert.InRange(Math.Abs(r - .1 * Math.Pow(1 + weight, 2)), 0, 1e-14);
    }
}
