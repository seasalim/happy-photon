using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class OpsContractTests
{
    [Fact]
    public void FractionalAreaCellsAndPyramidMatchIndependentOracles()
    {
        const int w = 319, h = 197;
        var source = OpsKernelTests.Sentinel(w, h);
        using var basis = RenderPipelineTestSupport.CreateBase(source, height: h);
        var wb = new double[,] { { 1.2, -.1, 0 }, { 0, .9, .1 }, { 0, .05, 1.1 } };
        var actual = OpsDehaze.Build(basis.Pixels, wb, 2);
        var expected = OpsDehazeOracle.Build(source, w, h, wb);
        for (var c = 0; c < 3; c++) for (var i = 0; i < actual.Color[c].Values.Length; i++)
            Assert.InRange(Math.Abs(actual.Color[c].Values[i] - expected.Color[c].Values[i]), 0, 1e-12);
        foreach (var candidate in Enum.GetValues<OpsClarity>())
        {
            using var image = new MagickImage(basis.Pixels);
            var arm = new OpsArm("CL", Clarity: 100);
            OpsPresencePrototype.Apply(image, basis.Info, arm, candidate, workers: 2);
            OpsKernelTests.MaxError(OpsPresenceOracle.Apply(source, w, h, w, arm, candidate),
                RenderPipelineTestSupport.ReadPixels(image), 1);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SuppliedAnalysisEqualsRebuiltAnalysisAndRemainsImmutable(bool refine)
    {
        using var basis = RenderPipelineTestSupport.CreateBase(OpsKernelTests.Sentinel(), true, 31);
        var analysis = OpsDehaze.Build(basis.Pixels, ChromaticAdaptation.Identity());
        var transmission = (double[])analysis.Transmission.Values.Clone();
        foreach (var arm in new[] { new OpsArm("DH+", Dehaze: 50), new OpsArm("DH-", Dehaze: -50), OpsArm.Stack })
        {
            using var rebuilt = OpsRenderHarness.Render(basis, arm, OpsClarity.Guided, refine);
            using var cached = OpsRenderHarness.Render(basis, arm, OpsClarity.Guided, refine, analysis: analysis);
            Assert.Equal(RenderPipelineTestSupport.ReadPixels(rebuilt), RenderPipelineTestSupport.ReadPixels(cached));
        }
        Assert.Equal(transmission, analysis.Transmission.Values);
    }

    [Fact]
    public void SharedAnalysisUsesFullFieldAtPreviewCellCentres()
    {
        var grid = new OpsGrid(2, 2, [.1, .3, .5, .7]);
        var full = new OpsDehaze([grid, grid, grid], grid, [.65, .7, .75]);
        var shared = full.Resample(3, 3);
        Assert.Equal(full.Airlight, shared.Airlight);
        double[] expected = [.1, .2, .3, .3, .4, .5, .5, .6, .7];
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], shared.Transmission.Values[i], 12);
            foreach (var color in shared.Color) Assert.Equal(expected[i], color.Values[i], 12);
        }
        Assert.Same(grid, full.Resample(2, 2).Transmission);
    }

    [Fact]
    public void FieldsSaturateAfterSummationAndUniformFieldEqualsGlobal()
    {
        const int w = 47, h = 31;
        var locals = Enumerable.Range(0, 8).Select(i => new LocalAdjustment
        { Type = "radial", Cu = .5, Cv = .5, Rx = 10, Ry = 10, Feather = 0 }).ToArray();
        var field = OpsAmountField.Create(w * h, locals, 30, 50)!;
        field.Fill(0, w, h, .2, .3, .4, locals);
        Assert.Equal(20000, field.Texture[0]); Assert.Equal(20000, field.Clarity[0]);
        field.Fill(0, w, h, .2, .3, .4, locals, -30, -50);
        Assert.Equal(-20000, field.Texture[0]); Assert.Equal(-20000, field.Clarity[0]);
        for (var i = 0; i < w * h; i++) field.Fill(i, w, h, .2, .3, .4, [locals[0]], 30, 50);
        using var basis = RenderPipelineTestSupport.CreateBase(OpsKernelTests.Sentinel(), height: h);
        using var global = new MagickImage(basis.Pixels); using var local = new MagickImage(basis.Pixels);
        OpsPresencePrototype.Apply(global, basis.Info, new("global", 30, 50), OpsClarity.Guided);
        OpsPresencePrototype.Apply(local, basis.Info, OpsArm.Locals, OpsClarity.Guided, field);
        OpsKernelTests.MaxError(RenderPipelineTestSupport.ReadPixels(global), RenderPipelineTestSupport.ReadPixels(local), 0);
    }

    [Fact]
    public void BothCandidatesAreBitIdenticalAcrossPartitions()
    {
        using var basis = RenderPipelineTestSupport.CreateBase(OpsKernelTests.Sentinel(), true, 31);
        var original = RenderPipelineTestSupport.ReadPixels(basis.Pixels);
        foreach (var candidate in Enum.GetValues<OpsClarity>()) foreach (var refine in new[] { false, true })
        foreach (var arm in new[] { OpsArm.Stack, OpsArm.Locals })
        {
            using var reference = OpsRenderHarness.Render(basis, arm, candidate, refine, workers: 1, bandRows: 1);
            foreach (var workers in new[] { 1, 2, Environment.ProcessorCount }.Distinct()) foreach (var rows in new[] { 1, 32 })
            {
                using var actual = OpsRenderHarness.Render(basis, arm, candidate, refine, workers: workers, bandRows: rows);
                OpsKernelTests.MaxError(RenderPipelineTestSupport.ReadPixels(reference), RenderPipelineTestSupport.ReadPixels(actual), 0);
            }
        }
        Assert.Equal(original, RenderPipelineTestSupport.ReadPixels(basis.Pixels));
    }
}
