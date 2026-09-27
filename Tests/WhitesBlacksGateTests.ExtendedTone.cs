using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class WhitesBlacksGateTests
{
    private void ExtendedToneBuild(BaseImageInfo info)
    {
        foreach (var curves in new[] { false, true })
        {
            var channel = new CurveData();
            if (curves) channel.AddPointAndReturnIndex(.4, .3);
            var raw = new AgxToneParameters(0, info.SourceExposureBiasEv, 0, 0, 0, new(), channel, channel, channel);
            var standard = new ToneParams(info.SourceExposureBiasEv, 1, 0, 0, 0, 0, false, new(), channel, channel, channel);
            ExtendedToneLut.Channels Compose() => info.IsRawSource
                ? ExtendedToneLut.ComposeRaw(raw, 1) : ExtendedToneLut.ComposeStandard(standard);
            var cold = new List<double>();
            for (var i = -1; i < 5; i++)
            {
                var elapsed = Time(() => { var table = Compose(); Assert.True(double.IsFinite(table.Red.Evaluate(.18))); });
                if (i >= 0) cold.Add(elapsed);
            }
            var key = info.IsRawSource ? AgxToneLut.ComposeCached(raw, 1) : ToneLut.ComposeCached(standard);
            ExtendedToneLut.Channels Cached() => info.IsRawSource
                ? ExtendedToneLut.ForRaw(key, raw, 1) : ExtendedToneLut.ForStandard(key, standard);
            var cached = Cached();
            var hit = Time(() => { for (var i = 0; i < 1000; i++) Assert.Same(cached, Cached()); }) / 1000;
            Report("R2-extended-tone-build", new { diagnostic = true, curves, coldMedianMs = Median(cold),
                cachedMs = hit, nodesPerChannel = ExtendedToneLut.Intervals + 1,
                note = "Cold build is cached by tone identity; Whites/Blacks amount changes do not rebuild it. Diagnostic only; lease qualification belongs to the reviewer." });
        }
    }
}
