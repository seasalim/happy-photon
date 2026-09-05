using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsContractPrototypeTests(ITestOutputHelper output)
{
    [Fact]
    public void Oracle_AnchorsToProductionNodesAndExtendedHandValues()
    {
        var raw = Raw();
        var standard = Standard();
        var lut = AgxToneLut.ComposeCached(raw, 1).Red;
        var standardLut = ToneLut.Compose(standard).Red;
        var rawError = 0;
        var standardError = 0;
        for (var i = 0; i <= 65535; i++)
        {
            var v = i / 65535.0;
            rawError = Math.Max(rawError, Math.Abs(Code(Encode(ReferenceRaw(v, raw))) - Code(Encode(lut[i]))));
            standardError = Math.Max(standardError, Math.Abs(Code(ReferenceStandard(v, standard)) - Code(standardLut[i])));
        }
        Assert.InRange(rawError, 0, 1);
        Assert.InRange(standardError, 0, 1);
        // Independently calculated log-window positions, plus exact grey and window endpoints.
        Assert.Equal(0.8166018902019643, (Math.Log2(2 / 0.18) + 10) / 16.5, 12);
        // 60-digit Decimal evaluation of the published tail formula, independently of this oracle.
        Assert.Equal(0.6922357299357824, ReferenceRaw(2, raw), 12);
        Assert.Equal(0.18, ReferenceRaw(0.18, raw), 12);
        Assert.InRange(ReferenceRaw(0.18 * Math.Pow(2, -10), raw), 0, 1e-12);
        Assert.Equal(1, ReferenceRaw(0.18 * Math.Pow(2, 6.5), raw), 12);
        Assert.True(ReferenceRaw(2, raw) > ReferenceRaw(1, raw));
        Assert.Equal(ReferenceRaw(2, raw), ReferenceRaw(0.5, raw, fold: 4), 12);
        Assert.Equal(ReferenceStandard(2, standard), ReferenceStandard(0.5, standard with { Fold = 4 }), 12);
        output.WriteLine($"Oracle anchors: RAW {rawError} codes; standard {standardError} codes; " +
            $"linear 2 window={(Math.Log2(2 / 0.18) + 10) / 16.5:F12}, tone={ReferenceRaw(2, raw):F12}.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Analytic_RecoversLocalPositiveThenGlobalNegative(bool isRaw)
    {
        var wb = ChromaticAdaptation.Identity();
        var kernel = new Kernel(Candidate.Analytic, Raw(-1), Standard(-1), wb, isRaw);
        var unedited = new Kernel(Candidate.Analytic, Raw(), Standard(), wb, isRaw);
        Local[] locals = [Local.Create(ev: 1)];
        var prepared = Prepare(locals);
        var boundedError = 0;
        var extendedError = 0;
        for (var i = 0; i <= 65535; i++)
        {
            var v = i / 65535.0;
            var bounded = new Rgb(v, v, v);
            boundedError = Math.Max(boundedError, Error(unedited.Legacy(bounded), kernel.Render(bounded, prepared)));
            var extended = bounded * 16;
            extendedError = Math.Max(extendedError, Error(
                Reference(extended, Raw(), Standard(), wb, [], isRaw), kernel.Render(extended, prepared)));
        }
        var sample = new Rgb(0.75, 0.75, 0.75);
        var naive = kernel.Legacy((sample * 2).Quantize());
        var naiveError = Error(unedited.Legacy(sample), naive);
        output.WriteLine($"G1 {(isRaw ? "RAW" : "standard")}: production 0–1 ramp {boundedError} codes; " +
            $"oracle 0–16 ramp {extendedError} codes; naive v=0.75 error {naiveError} codes.");
        Assert.InRange(boundedError, 0, 1);
        Assert.InRange(extendedError, 0, 1);
        Assert.True(naiveError > 1000);
    }

    [Fact]
    public void NeutralLocals_BypassBitExactlyIncludingValueDependentDcp()
    {
        var samples = Samples().Where(v => v.R <= 1 && v.G <= 1 && v.B <= 1)
            .Select(v => v.Quantize()).ToArray();
        var wb = WhiteBalanceModel.CreateGainMatrix([1.2, 1, 0.8]);
        var neutral = Prepare([Local.Create(), Local.Create(ev: 4, weight: 0, mired: 50)]);
        Assert.Empty(neutral);
        foreach (var isRaw in new[] { true, false })
        foreach (var map in new DcpHueSatMap?[] { null, HueSatMap() })
        {
            var source = Flatten(samples);
            using var image = RenderPipelineTestSupport.CreateBase(source);
            if (isRaw) new AgxCrossing(Raw(), wb, map).Apply(image.Pixels);
            else
            {
                DcpHueSatRenderer.Apply(image.Pixels, map);
                var normalized = ChromaticAdaptation.NormalizeForRender(wb);
                ToneLutApplicator.Apply(image.Pixels, normalized.Matrix,
                    ToneLut.Compose(Standard() with { Fold = normalized.Fold }));
            }
            var expected = RenderPipelineTestSupport.ReadPixels(image.Pixels);
            var kernel = new Kernel(Candidate.Analytic, Raw(), Standard(), wb, isRaw);
            var actual = Flatten(samples.Select(v => kernel.Render(Dcp(v, map), neutral)).ToArray());
            Assert.Equal(expected, actual);
            output.WriteLine($"G4 bypass {(isRaw ? "RAW" : "standard")}, DCP={map != null}: " +
                $"{samples.Length} pixels, 0 differing codes.");
        }
    }

    [Fact]
    public void CreationOrder_MonochromeAndLinearResultBlendAreExplicit()
    {
        var wb = ChromaticAdaptation.Identity();
        var kernel = new Kernel(Candidate.Analytic, Raw(), Standard(), wb);
        Local[] order = [Local.Create(mired: 50), Local.Create(saturation: -80)];
        var sample = new Rgb(0.6, 0.3, 0.1);
        var forward = kernel.Render(sample, Prepare(order));
        var reverse = kernel.Render(sample, Prepare(order.Reverse().ToArray()));
        Assert.InRange(Error(forward, Reference(sample, Raw(), Standard(), wb, order)), 0, 1);
        var orderError = Error(forward, reverse);
        Assert.True(orderError >= 1);
        Local[] exposure = [Local.Create(ev: 1, weight: 0.5), Local.Create(ev: -1, weight: 0.5)];
        var commutingError = Error(kernel.Render(sample, Prepare(exposure)),
            kernel.Render(sample, Prepare(exposure.Reverse().ToArray())));
        Assert.Equal(0, commutingError);
        var monoKernel = new Kernel(Candidate.Analytic, Raw(), Standard(),
            WhiteBalanceModel.CreateGainMatrix([1.2, 1, 0.8]));
        var color = Prepare([Local.Create(mired: 50, tint: -50, saturation: 100)]);
        var colorExposure = Prepare([Local.Create(ev: 1, mired: 50, tint: -50, saturation: 100)]);
        for (var i = 0; i <= 1024; i++)
        {
            var v = i / 1024.0;
            var gray = new Rgb(v, v, v);
            Assert.Equal(gray, CandidateLocals(gray, color, mono: true));
            var rendered = monoKernel.Render(gray, color, mono: true).Quantize();
            Assert.Equal(rendered.R, rendered.G);
            Assert.Equal(rendered.G, rendered.B);
            Assert.Equal(monoKernel.Render(gray, Prepare([Local.Create(ev: 1)]), mono: true),
                monoKernel.Render(gray, colorExposure, mono: true));
        }
        output.WriteLine($"G4 pinned Temp→Sat vs Sat→Temp: {orderError} codes; exposure-only pair: " +
            $"{commutingError} codes (commutes). Monochrome: 1025 equal-channel samples; color controls exact no-ops.");
        foreach (var ev in new[] { -1.0, 1.0 })
        {
            var linearGain = 1 + 0.5 * (Math.Pow(2, ev) - 1);
            var actual = CandidateLocals(new(1, 1, 1), Prepare([Local.Create(ev, 0.5)]), false);
            Assert.Equal(linearGain, actual.R);
            output.WriteLine($"Blend {ev:+0;-0} EV w=0.5: linear gain={linearGain:F9}, " +
                $"EV={Math.Log2(linearGain):F9}; EV-blend gain={Math.Pow(2, ev * 0.5):F9}, EV={ev * 0.5:F9}.");
        }
    }

    [Fact]
    public void HueSat_SeesPreLocalQ16AndItsApproximationIsSeparate()
    {
        var map = HueSatMap();
        var wb = ChromaticAdaptation.Identity();
        var kernel = new Kernel(Candidate.Analytic, Raw(), Standard(), wb);
        Local[] local = [Local.Create(ev: 1)];
        var prepared = Prepare(local);
        var approximation = new Errors();
        var order = new Errors();
        var unclippedOrder = new Errors();
        var fidelity = new Errors();
        var sceneCodeError = 0;
        foreach (var input in Samples().Where(v => v.R <= 1 && v.G <= 1 && v.B <= 1))
        {
            var v = input.Quantize();
            var dcp = Dcp(v, map);
            var analytic = DcpHueSatRenderer.TransformWorkingRgb(map, v.R, v.G, v.B);
            // Compare bounded scene-linear Q16 DCP outputs through neutral tone for ΔE00.
            var exact = new Rgb(analytic.Red, analytic.Green, analytic.Blue).Quantize();
            sceneCodeError = Math.Max(sceneCodeError, Error(exact, dcp));
            approximation.Add(kernel.Legacy(exact), kernel.Legacy(dcp));
            var expected = Reference(dcp, Raw(), Standard(), wb, local);
            var actual = kernel.Render(dcp, prepared);
            fidelity.Add(expected, actual);
            order.Add(actual, kernel.Render(Dcp(v * 2, map), []));
            if (Math.Max(v.R, Math.Max(v.G, v.B)) <= 0.5)
                unclippedOrder.Add(actual, kernel.Render(Dcp(v * 2, map), []));
        }
        output.WriteLine($"G4 HueSat-first candidate: {fidelity}; reversed (Q16-clipped local→DCP): {order}.");
        output.WriteLine($"DCP lattice vs analytic map, separately measured through neutral RAW tone: {approximation}.");
        output.WriteLine($"DCP direct scene Q16 max error={sceneCodeError}; reversed order with local input ≤0.5 " +
            $"(no gain write-back clipping): {unclippedOrder}.");
        Assert.InRange(fidelity.MaxCode, 0, 1);
        Assert.True(order.MaxCode > 0);
        Assert.True(unclippedOrder.MaxCode > 0);
    }

    private static DcpHueSatMap HueSatMap()
    {
        var table = new float[6 * 3 * 3 * 3];
        for (var v = 0; v < 3; v++)
        for (var h = 0; h < 6; h++)
        for (var s = 0; s < 3; s++)
        {
            var i = ((v * 6 + h) * 3 + s) * 3;
            table[i] = s == 0 ? 0 : (v - 1) * 12 + h;
            table[i + 1] = s == 0 ? 1 : 0.85f + 0.1f * v;
            table[i + 2] = 0.7f + 0.15f * v;
        }
        return DcpHueSatRenderer.Prepare(new(6, 3, 3, false, table, null, 0));
    }

    private static Rgb Dcp(Rgb v, DcpHueSatMap? map)
    {
        if (map == null) return v;
        var result = DcpHueSatRenderer.EvaluateLut(map, [v.R, v.G, v.B]);
        return new(result[0], result[1], result[2]);
    }

    private static ushort[] Flatten(Rgb[] values) => values.SelectMany(v =>
        new[] { (ushort)Code(v.R), (ushort)Code(v.G), (ushort)Code(v.B) }).ToArray();

    private static int Error(Rgb a, Rgb b) => Math.Max(Math.Abs(Code(a.R) - Code(b.R)),
        Math.Max(Math.Abs(Code(a.G) - Code(b.G)), Math.Abs(Code(a.B) - Code(b.B))));
}
