using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;

namespace HappyPhoton.Tests;

// Whole-frame, no-geometry WP1 seam only. It deliberately refuses unsupported
// arrangements rather than silently qualifying a second general render pipeline.
internal static class OpsRenderHarness
{
    internal static MagickImage Render(BaseImage basis, OpsArm arm, OpsClarity candidate,
        bool refine = false, RenderIntent intent = RenderIntent.Export, int workers = 1, int bandRows = 32, OpsDehaze? analysis = null)
    {
        using var upstream = Upstream(basis, OpsWorkloads.Settings(arm), arm, candidate, refine, intent, workers, bandRows, analysis: analysis);
        return RenderFinalizer.Finalize(upstream, null, OutputColorSpace.Srgb, OutputSharpeningMode.Off, false);
    }

    internal static MagickImage Upstream(BaseImage basis, EditSettings settings, OpsArm arm, OpsClarity candidate,
        bool refine, RenderIntent intent, int workers = 1, int bandRows = 32, bool forceFused = false, OpsDehaze? analysis = null)
    {
        var deferred = settings.Clone(); deferred.Detail.CaptureSharpen = 0;
        OpsAmountField? field = null;
        MagickImage image;
        if (arm.Dehaze == 0 && !arm.Local && !forceFused)
            image = new RenderPipeline().RenderDisplayRec2020(new(basis, deferred, intent, null, new(false, false)));
        else
        {
            image = new MagickImage(basis.Pixels);
            try
            {
                field = Fused(image, basis.Info, settings, arm, refine, workers, bandRows, analysis);
                RenderColorEncoding.RetagAsSrgb(image);
                if (!basis.Info.IsMonochrome) RenderChromaStage.Apply(image, settings);
                RenderNoiseReduction.Apply(image, basis.Info, settings.Detail);
            }
            catch { image.Dispose(); throw; }
        }
        try
        {
            OpsPresencePrototype.Apply(image, basis.Info, arm, candidate, field, workers, bandRows);
            RenderSharpening.ApplyCapture(image, basis.Info, settings.Detail, intent);
            return image;
        }
        catch { image.Dispose(); throw; }
    }

    internal static OpsAmountField? Fused(MagickImage image, BaseImageInfo info, EditSettings settings,
        OpsArm arm, bool refine, int workers, int bandRows, OpsDehaze? analysis = null)
    {
        if (info.DcpProfile?.HueSatMap != null || settings.Crop != null || settings.Geometry != null)
            throw new NotSupportedException("OPS-WP1 harness requires an uncropped base without a DCP HueSat map.");
        var wb = RenderChromaticStage.CreateWhiteBalanceMatrix(info, settings);
        var haze = arm.Dehaze == 0 ? null : analysis ?? OpsDehaze.Build(image, wb, workers);
        using var pixels = image.GetPixels();
        var w = (int)image.Width; var h = (int)image.Height;
        var layout = RenderKernelSupport.GetLayout(pixels);
        var values = pixels.GetArea(0, 0, image.Width, image.Height)!;
        var field = arm.Local ? OpsAmountField.Create(w * h, settings.Locals, 30, 50) : null;
        var tone = new OpsTone(info, settings);
        // Geometry is identity, but use the real trace to retain locals' production ownership.
        using var geometry = RenderGeometry.Apply(image, settings, out var trace);
        var locals = RenderLocals.Create(settings, trace, w, h, info: info);
        Parallel.For(0, (h + bandRows - 1) / bandRows, new ParallelOptions { MaxDegreeOfParallelism = workers }, band =>
        {
            for (var y = band * bandRows; y < Math.Min(h, (band + 1) * bandRows); y++)
            for (var x = 0; x < w; x++)
            {
                var i = y * w + x; var offset = i * layout.Channels;
                var r = values[offset + layout.Red] / 65535d; var g = values[offset + layout.Green] / 65535d;
                var b = values[offset + layout.Blue] / 65535d;
                var cr = wb[0, 0] * r + wb[0, 1] * g + wb[0, 2] * b;
                var cg = wb[1, 0] * r + wb[1, 1] * g + wb[1, 2] * b;
                var cb = wb[2, 0] * r + wb[2, 1] * g + wb[2, 2] * b;
                haze?.Correct(ref cr, ref cg, ref cb, (x + .5) / w, (y + .5) / h, arm.Dehaze, refine);
                field?.Fill(i, w, h, cr, cg, cb, settings.Locals!);
                var changed = haze != null;
                if (locals != null) changed |= locals.ApplyColor(i, ref cr, ref cg, ref cb);
                var result = changed ? tone.Extended(cr, cg, cb) : tone.Off(r, g, b);
                values[offset + layout.Red] = result.R; values[offset + layout.Green] = result.G; values[offset + layout.Blue] = result.B;
            }
        });
        pixels.SetArea(0, 0, image.Width, image.Height, values);
        return field;
    }
}

internal sealed class OpsTone
{
    private readonly bool raw;
    private readonly AgxToneParameters agx;
    private readonly ToneParams standard;
    private readonly AgxCrossing.Matrix3x3 wb, inset = new(AgxToneEngine.InsetMatrix), outset = new(AgxToneEngine.OutsetMatrix);
    private readonly AgxCrossing? crossing;
    private readonly ToneLuts luts;
    private readonly double exposure, slope, toe, shoulder, toeScale, shoulderScale;
    private readonly bool identity;

    internal OpsTone(BaseImageInfo info, EditSettings settings)
    {
        raw = info.IsRawSource;
        var matrix = RenderChromaticStage.CreateWhiteBalanceMatrix(info, settings);
        var normalized = ChromaticAdaptation.NormalizeForRender(matrix); wb = new(normalized.Matrix);
        agx = new(settings.Exposure, info.SourceExposureBiasEv, settings.Contrast, settings.Highlights,
            settings.Shadows, settings.Curve, settings.CurveRed, settings.CurveGreen, settings.CurveBlue);
        crossing = raw ? new(agx, matrix) : null;
        standard = new(settings.Exposure + info.SourceExposureBiasEv, normalized.Fold, settings.Brightness,
            settings.Contrast, settings.Shadows, settings.Highlights, settings.BaseLook ?? false,
            settings.Curve, settings.CurveRed, settings.CurveGreen, settings.CurveBlue);
        luts = ToneLut.ComposeCached(standard);
        exposure = Math.Pow(2, agx.ExposureEv + agx.SourceExposureEv);
        slope = AgxToneEngine.Slope(agx.Contrast); toe = AgxToneEngine.ToePower(agx.Shadows);
        shoulder = AgxToneEngine.ShoulderPower(agx.Highlights);
        toeScale = AgxToneEngine.TailScale(AgxToneEngine.XPivot, AgxToneEngine.YPivot, slope, toe);
        shoulderScale = AgxToneEngine.TailScale(1 - AgxToneEngine.XPivot, 1 - AgxToneEngine.YPivot, slope, shoulder);
        identity = settings.Curve.IsIdentity() && settings.CurveRed == null && settings.CurveGreen == null && settings.CurveBlue == null;
    }

    internal (ushort R, ushort G, ushort B) Off(double r, double g, double b)
    {
        if (raw)
        {
            var v = crossing!.TransformInterpolated(new(r, g, b));
            return (OpsWorkloads.Q(v.Red), OpsWorkloads.Q(v.Green), OpsWorkloads.Q(v.Blue));
        }
        return (OpsWorkloads.Q(ToneLutApplicator.Interpolate(luts.Red, wb.Row0(r, g, b))),
            OpsWorkloads.Q(ToneLutApplicator.Interpolate(luts.Green, wb.Row1(r, g, b))),
            OpsWorkloads.Q(ToneLutApplicator.Interpolate(luts.Blue, wb.Row2(r, g, b))));
    }

    internal (ushort R, ushort G, ushort B) Extended(double r, double g, double b)
    {
        if (!raw)
        {
            var p = standard with { Fold = 1 };
            // Match the standard production tone boundary; keep signed values until here.
            r = Math.Max(0, r); g = Math.Max(0, g); b = Math.Max(0, b);
            return (OpsWorkloads.Q(ToneLut.Evaluate(p, r, p.CurveRed)), OpsWorkloads.Q(ToneLut.Evaluate(p, g, p.CurveGreen)),
                OpsWorkloads.Q(ToneLut.Evaluate(p, b, p.CurveBlue)));
        }
        var tr = Tone(inset.Row0(r, g, b), agx.CurveRed);
        var tg = Tone(inset.Row1(r, g, b), agx.CurveGreen);
        var tb = Tone(inset.Row2(r, g, b), agx.CurveBlue);
        return (OpsWorkloads.Q(ToneLut.SrgbEncode(Math.Clamp(outset.Row0(tr, tg, tb), 0, 1))),
            OpsWorkloads.Q(ToneLut.SrgbEncode(Math.Clamp(outset.Row1(tr, tg, tb), 0, 1))),
            OpsWorkloads.Q(ToneLut.SrgbEncode(Math.Clamp(outset.Row2(tr, tg, tb), 0, 1))));
    }

    private double Tone(double value, CurveData? curve) => AgxToneEngine.EvaluateToneExtendedUnchecked(value,
        agx, exposure, 0, slope, toe, shoulder, curve, toeScale, shoulderScale, identity);
}
