using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LocalBrushRenderTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0, 0)] [InlineData(90, 2)] [InlineData(270, -3)]
    public void OracleAgreementThroughProductionWithCropAndFrameOverride(int rotation, double horizon)
    {
        using var image = new MagickImage(MagickColors.Gray, 93, 61);
        var document = LocalsBrushProduction.Quantized(LocalsBrushWorkloads.Create(false, 93, 61)[0]);
        var local = new LocalAdjustment { Type = "brush", Exposure = 1, Strokes = LocalsBrushProduction.Strokes(document) };
        var settings = new EditSettings { Rotation = rotation, HorizonRotation = horizon,
            Crop = new() { Left = .12, Top = .08, Right = .91, Bottom = .85 }, Locals = [local] };
        using var geometry = RenderGeometry.Apply(image, settings, out var trace);
        var frame = RenderGeometry.CalculateLocalsFrame(93, 61, settings);
        const int width = 47, height = 29;
        var plan = RenderLocals.Create(settings, trace, width, height)!;
        var resting = RenderLocals.Create(settings, default, width, height, frame)!;
        var export = RenderLocals.Create(settings, trace, width * 3, height * 3)!;
        double worst = 0;
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var u = (trace.CropX + (x + .5) * trace.Width / width) / trace.CorrectedFrameWidth;
            var v = (trace.CropY + (y + .5) * trace.Height / height) / trace.CorrectedFrameHeight;
            var expected = LocalsBrushOracle.Weight(document, u, v, trace.CorrectedFrameWidth, trace.CorrectedFrameHeight);
            var p = y * width + x;
            worst = Math.Max(worst, Math.Abs(expected - (plan.Gain(p) - 1)));
            Assert.InRange(Math.Abs(expected - (resting.Gain(p) - 1)), 0, 1e-10);
            Assert.InRange(Math.Abs(plan.Gain(p) - export.Gain((y * 3 + 1) * width * 3 + x * 3 + 1)), 0, 1e-10);
        }
        output.WriteLine($"production_crop_oracle max_absolute_error={worst:R} rotation={rotation} horizon={horizon}");
        Assert.InRange(worst, 0, 1e-10);
        local.Luminance = new() { Enabled = true, Lower = .2, Upper = .8, Softness = .1 };
        local.Hue = new() { Enabled = true, Center = 45, Width = 180, Softness = 30 };
        plan = RenderLocals.Create(settings, trace, width, height)!;
        for (var p = 0; p < width * height; p++)
        {
            double r = .45, g = .2, b = .12;
            var u = (trace.CropX + (p % width + .5) * trace.Width / width) / trace.CorrectedFrameWidth;
            var v = (trace.CropY + (p / width + .5) * trace.Height / height) / trace.CorrectedFrameHeight;
            var expected = LocalsBrushOracle.RangedWeight(document, u, v, trace.CorrectedFrameWidth,
                trace.CorrectedFrameHeight, r, g, b, new(.2, .8, .1), new(45, 180, 30));
            plan.ApplyColor(p, ref r, ref g, ref b);
            Assert.InRange(Math.Abs(expected - (r / .45 - 1)), 0, 1e-10);
        }
    }

    [Theory]
    [InlineData(false, false, false)] [InlineData(true, false, false)]
    [InlineData(false, true, false)] [InlineData(true, true, false)]
    [InlineData(false, false, true)] [InlineData(true, false, true)]
    [InlineData(false, true, true)] [InlineData(true, true, true)]
    public void SingleDabMatchesCircularRadialInBothKernelsAndResting(bool raw, bool colorRange, bool resting)
    {
        const int width = 120, height = 80;
        var samples = Enumerable.Range(0, width * height * 3).Select(i => (ushort)(2000 + i * 713 % 35000)).ToArray();
        using var basis = RenderPipelineTestSupport.CreateBase(samples, raw, height);
        var local = new LocalAdjustment { Type = "brush", Exposure = 1.3,
            Strokes = [new() { Radius = .25, Feather = .5, Flow = 1, Points = [new(8192, 8192)] }],
            Temperature = colorRange ? 25 : 0, Tint = colorRange ? -12 : 0, Saturation = colorRange ? 35 : 0,
            Luminance = colorRange ? new() { Enabled = true, Lower = .2, Upper = .9, Softness = .1 } : null,
            Hue = colorRange ? new() { Enabled = true, Center = 30, Width = 300, Softness = 30 } : null };
        var brush = new EditSettings { Locals = [local], Detail = new() { CaptureSharpen = 0 } };
        var radial = brush.Clone(); radial.Locals![0] = local with { Type = "radial", Cu = .5, Cv = .5,
            Rx = .25, Ry = .25, Feather = .5, Angle = 0, Strokes = null };
        var off = brush.Clone(); off.Locals = null;
        // The real resting pipeline sees already prepared pixels and the full corrected-frame override.
        LocalsFrame? frame = resting ? new(240, 160, .125, .125, .75, .75) : null;
        var pipeline = new RenderPipeline();
        RenderResult Run(EditSettings settings)
        {
            var request = new RenderRequest(basis, settings, RenderIntent.Preview, null, new(false, false))
                { LocalsFrameOverride = frame };
            return resting ? pipeline.RenderResting(request, RenderExecutionOptions.Resting(CancellationToken.None)) : pipeline.Render(request);
        }
        using var a = Run(brush); using var b = Run(radial); using var c = Run(off);
        var actual = RenderPipelineTestSupport.ReadPixels(a.Image);
        Assert.Equal(RenderPipelineTestSupport.ReadPixels(b.Image), actual);
        var neutral = RenderPipelineTestSupport.ReadPixels(c.Image);
        Assert.NotEqual(neutral, actual);
        using var geometry = RenderGeometry.Apply(basis.Pixels, brush, out var trace);
        var mask = RenderLocals.Create(brush, trace, width, height, frame)!;
        var untouched = 0;
        for (var p = 0; p < width * height; p++)
            if (mask.Gain(p) == 1)
            {
                untouched++;
                for (var channel = 0; channel < 3; channel++) Assert.Equal(neutral[p * 3 + channel], actual[p * 3 + channel]);
            }
        Assert.True(untouched > width * height / 4);
        Assert.Equal(samples, RenderPipelineTestSupport.ReadPixels(basis.Pixels));

        var gradient = new LocalAdjustment { Cu = .7, Angle = 30, Feather = .3, Exposure = -.5, Temperature = -35 };
        brush.Locals.Add(gradient); radial.Locals!.Add(gradient with { });
        using var mixedBrush = Run(brush); using var mixedRadial = Run(radial);
        Assert.Equal(RenderPipelineTestSupport.ReadPixels(mixedRadial.Image), RenderPipelineTestSupport.ReadPixels(mixedBrush.Image));
        brush.Locals.Reverse(); radial.Locals.Reverse();
        using var reversedBrush = Run(brush); using var reversedRadial = Run(radial);
        Assert.Equal(RenderPipelineTestSupport.ReadPixels(reversedRadial.Image), RenderPipelineTestSupport.ReadPixels(reversedBrush.Image));
        if (colorRange) Assert.NotEqual(RenderPipelineTestSupport.ReadPixels(mixedBrush.Image), RenderPipelineTestSupport.ReadPixels(reversedBrush.Image));
    }

    [Fact]
    public void EmptyDisabledAndNeutralBrushesDoNotCreatePlans()
    {
        var settings = new EditSettings { Locals = [new() { Type = "brush", Exposure = 2 }] };
        var frame = new LocalsFrame(40, 30, 0, 0, 1, 1);
        Assert.Null(RenderLocals.Create(settings, default, 40, 30, frame));
        Assert.Equal(0, new LocalBrushEvaluator(null, 40, 30).Weight(.5, .5));
        settings.Locals[0].Strokes = [];
        Assert.Null(RenderLocals.Create(settings, default, 40, 30, frame));
        settings.Locals[0].Strokes = [new() { Points = [new(8192, 8192)] }];
        settings.Locals[0].Enabled = false;
        Assert.Null(RenderLocals.Create(settings, default, 40, 30, frame));
        settings.Locals[0].Enabled = true; settings.Locals[0].Exposure = 0;
        Assert.Null(RenderLocals.Create(settings, default, 40, 30, frame));
    }
}
