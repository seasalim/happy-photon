using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WhitesBlacksTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SliderGridIsMonotoneAndFixesGreyAndOppositeHalf(bool raw)
    {
        foreach (var whites in Enumerable.Range(-10, 21).Select(i => i * 10))
        foreach (var blacks in Enumerable.Range(-10, 21).Select(i => i * 10))
        {
            var op = new WhitesBlacksOperator(whites, blacks, raw);
            Assert.Equal(1, op.Gain(WhitesBlacksOperator.Position(.18)));
            var previous = 0d;
            for (var i = 0; i <= 17408; i++)
            {
                var y = .18 * Math.Pow(2, -10 + i / 1024d);
                var gain = op.Gain(WhitesBlacksOperator.Position(y));
                var value = y * gain;
                Assert.True(value > previous, $"raw={raw}, W={whites}, B={blacks}, node={i}");
                if (whites == 0 && y >= .18 || blacks == 0 && y <= .18) Assert.Equal(1, gain);
                previous = value;
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FrozenDisplayEndpointsMeetTargets(bool raw)
    {
        var positive = new WhitesBlacksOperator(100, 0, raw);
        var negative = new WhitesBlacksOperator(0, -100, raw);
        var white = Scene(.95); var black = Scene(.05);
        Assert.True(Display(white * positive.Gain(WhitesBlacksOperator.Position(white))) >= .99);
        Assert.True(Display(black * negative.Gain(WhitesBlacksOperator.Position(black))) <= .01);
        double Scene(double display)
        {
            double lo = 0, hi = 32;
            for (var i = 0; i < 80; i++) { var mid = (lo + hi) / 2; if (Display(mid) < display) lo = mid; else hi = mid; }
            return (lo + hi) / 2;
        }
        double Display(double value) => raw ? ToneLut.SrgbEncode(AgxToneEngine.EvaluateToneExtendedUnchecked(
            value, new(0, 0, 0, 0, 0, new()), 1, 0, 2, 3, 3.25)) : ToneLut.SrgbEncode(Math.Min(1, value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullFrameLocalEqualsGlobalWithExposureAndWhiteBalance(bool raw)
    {
        using var basis = RenderPipelineTestSupport.CreateBase(
            Enumerable.Range(0, 768).Select(i => (ushort)(i * 83)).ToArray(), raw, sourceBiasEv: raw ? 1.3 : 0);
        var settings = new EditSettings { Whites = 50, Blacks = -50, Exposure = .7,
            Detail = new() { CaptureSharpen = 0 }, Wb = new() { Mode = WbMode.Picked, Gains = [1.5, 1, .8] } };
        var local = settings.Clone(); local.Whites = local.Blacks = 0;
        local.Locals = [new() { Whites = 50, Blacks = -50, Angle = 0, Cu = 2, Feather = .001 }];
        using var globalImage = new RenderPipeline().RenderDisplayRec2020(new(basis, settings, RenderIntent.Export, null, new(false, false)));
        using var localImage = new RenderPipeline().RenderDisplayRec2020(new(basis, local, RenderIntent.Export, null, new(false, false)));
        Assert.All(RenderPipelineTestSupport.ReadPixels(globalImage).Zip(RenderPipelineTestSupport.ReadPixels(localImage)),
            pair => Assert.InRange(Math.Abs(pair.First - pair.Second), 0, 1));
        Assert.Equal(RenderPipelineTestSupport.ReadPixels(basis.Pixels),
            Enumerable.Range(0, 768).Select(i => (ushort)(i * 83)).ToArray());
    }

    [Fact]
    public void ExpandedRawGainReachesAnalyticToneWithoutClipping()
    {
        const ushort sample = 45875;
        using var basis = RenderPipelineTestSupport.CreateBase([sample, sample, sample], true, sourceBiasEv: 3);
        var settings = new EditSettings { Whites = 100, Detail = new() { CaptureSharpen = 0 } };
        using var image = new RenderPipeline().RenderDisplayRec2020(new(basis, settings, RenderIntent.Export, null, new(false, false)));
        var value = sample / 65535d;
        var gain = new WhitesBlacksOperator(100, 0, true).Gain(WhitesBlacksOperator.Position(value * 8));
        Assert.True(value * gain > 1);
        var expected = ToneLut.SrgbEncode(AgxToneEngine.EvaluateToneExtendedUnchecked(
            value * gain, new(0, 3, 0, 0, 0, new()), 8, 0, 2, 3, 3.25));
        Assert.All(RenderPipelineTestSupport.ReadPixels(image), code => Assert.InRange(Math.Abs(code - expected * 65535), 0, 1));
    }

    [Fact]
    public void MonochromeRawKeepsExactEqualChannels()
    {
        using var basis = RenderPipelineTestSupport.CreateBase(Enumerable.Range(0, 256)
            .SelectMany(i => Enumerable.Repeat((ushort)(i * 257), 3)).ToArray(), true, isMonochrome: true);
        foreach (var amount in new[] { -100, -50, 50, 100 })
        {
            var settings = new EditSettings { Whites = amount, Blacks = -amount, Detail = new() { CaptureSharpen = 0 },
                Locals = [new() { Whites = amount, Blacks = amount, Temperature = 20, Saturation = 40 }] };
            using var result = new RenderPipeline().Render(new(basis, settings, RenderIntent.Export, null, new(false, false)));
            foreach (var rgb in RenderPipelineTestSupport.ReadPixels(result.Image).Chunk(3))
            { Assert.Equal(rgb[0], rgb[1]); Assert.Equal(rgb[1], rgb[2]); }
        }
    }

    [Fact]
    public void ZeroAndDisabledPointsNeedNoRenderPlan()
    {
        using var basis = RenderPipelineTestSupport.CreateBase([0, 0, 0]);
        var settings = new EditSettings { Locals = [new() { Whites = 100, Blacks = -100, Enabled = false }] };
        using var geometry = RenderGeometry.Apply(basis.Pixels, settings, out var trace);
        Assert.Null(RenderLocals.Create(settings, trace, 1, 1));
        settings.Locals = null;
        Assert.Null(RenderLocals.Create(settings, trace, 1, 1));
        Assert.Equal(DocumentBoundaryGoldens.Neutral, EditSettingsJson.Serialize(settings));
    }

    [Fact]
    public async Task SettingsRoundTripClampTransferPresetsAndLabels()
    {
        var settings = new EditSettings { Whites = 50, Blacks = -50, Locals = [new() { Whites = 30, Blacks = -20 }] };
        Assert.True(settings.HasEdits);
        Assert.False(settings.HasSameEdits(new()));
        var json = EditSettingsJson.Serialize(settings);
        Assert.EndsWith("\"whites\":50,\"blacks\":-50}", json);
        Assert.True(settings.HasSameEdits(EditSettingsJson.Deserialize(json, out _)));
        Assert.True(settings.HasSameEdits(settings.Clone()));
        var clamped = EditSettingsJson.Deserialize(json.Replace(":50", ":200").Replace(":-50", ":-200")
            .Replace(":30", ":200").Replace(":-20", ":-200"), out var changed);
        Assert.True(changed); Assert.Equal(100, clamped.Whites); Assert.Equal(-100, clamped.Blacks);
        Assert.Equal(100, clamped.Locals![0].Whites); Assert.Equal(-100, clamped.Locals[0].Blacks);
        var target = new EditSettings { Locals = [new() { Whites = -40 }] };
        var destinationLocal = target.Locals[0];
        EditSettingsTransfer.ApplySubset(settings, target);
        Assert.Equal(50, target.Whites); Assert.Equal(-50, target.Blacks); Assert.Same(destinationLocal, target.Locals![0]);
        Assert.Null(EditSettingsTransfer.CopySubset(settings).Locals);
        using var directory = new TemporaryDirectory();
        var presets = new PresetService(); await presets.UseDirectoryAsync(directory.Path);
        await presets.SaveUserPresetAsync("Points", settings);
        var reloaded = new PresetService(); await reloaded.UseDirectoryAsync(directory.Path);
        var saved = Assert.Single(reloaded.UserPresets).Settings;
        Assert.Equal(50, saved.Whites); Assert.Equal(-50, saved.Blacks); Assert.Null(saved.Locals);
        Assert.Equal("Whites +50 (+50)", EditHistoryLabel.Derive(new(), new() { Whites = 50 }));
        var after = settings.Clone(); after.Locals![0].Blacks = -60;
        Assert.Equal("Linear 1 blacks -60 (-40)", EditHistoryLabel.Derive(settings, after));
    }
}
