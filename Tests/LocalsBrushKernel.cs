using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;

namespace HappyPhoton.Tests;

// Mirrors AgxCrossing.Locals / ToneLutApplicator.Locals. The production tone helpers
// are intentionally used; only the brush spatial definition/oracle is independent.
internal static class LocalsBrushKernel
{
    internal static void Apply(MagickImage image, BaseImageInfo info, EditSettings settings, LocalsBrushPlan? plan)
    {
        using var pixels = image.GetPixels();
        var values = pixels.GetArea(0, 0, image.Width, image.Height)!;
        var layout = RenderKernelSupport.GetLayout(pixels);
        var count = checked((int)(image.Width * image.Height));
        if (info.IsRawSource && !info.IsMonochrome && info.DcpProfile?.HueSatMap is { } map)
            DcpHueSatRenderer.ApplyValues(values, count, layout.Channels, layout.Red, layout.Green, layout.Blue, map);
        var raw = info.IsRawSource;
        var wb = RenderChromaticStage.CreateWhiteBalanceMatrix(info, settings);
        var composed = raw ? ChromaticAdaptation.Multiply(AgxToneEngine.InsetMatrix, wb) : wb;
        var normalized = ChromaticAdaptation.NormalizeForRender(composed);
        var input = new AgxCrossing.Matrix3x3(normalized.Matrix);
        var localWb = (double[,])wb.Clone();
        for (var r = 0; r < 3; r++) for (var c = 0; c < 3; c++) localWb[r, c] /= normalized.Fold;
        var basis = new AgxCrossing.Matrix3x3(localWb);
        var inset = new AgxCrossing.Matrix3x3(AgxToneEngine.InsetMatrix);
        var outset = new AgxCrossing.Matrix3x3(AgxToneEngine.OutsetMatrix);
        var parameters = new AgxToneParameters(settings.Exposure, info.SourceExposureBiasEv,
            settings.Contrast, settings.Highlights, settings.Shadows, settings.Curve,
            info.IsMonochrome ? null : settings.CurveRed, info.IsMonochrome ? null : settings.CurveGreen,
            info.IsMonochrome ? null : settings.CurveBlue);
        var standard = new ToneParams(settings.Exposure + info.SourceExposureBiasEv, normalized.Fold,
            settings.Brightness, settings.Contrast, settings.Shadows, settings.Highlights, settings.BaseLook ?? false,
            settings.Curve, settings.CurveRed, settings.CurveGreen, settings.CurveBlue);
        var luts = raw ? AgxToneLut.ComposeCached(parameters, normalized.Fold) : ToneLut.ComposeCached(standard);
        var exposure = Math.Pow(2, settings.Exposure + info.SourceExposureBiasEv);
        var log2Fold = Math.Log2(normalized.Fold);
        var slope = AgxToneEngine.Slope(settings.Contrast);
        var toe = AgxToneEngine.ToePower(settings.Shadows); var shoulder = AgxToneEngine.ShoulderPower(settings.Highlights);
        var toeScale = AgxToneEngine.TailScale(AgxToneEngine.XPivot, AgxToneEngine.YPivot, slope, toe);
        var shoulderScale = AgxToneEngine.TailScale(1 - AgxToneEngine.XPivot, 1 - AgxToneEngine.YPivot, slope, shoulder);
        var identity = settings.Curve.IsIdentity();
        var ri = identity && (parameters.CurveRed?.IsIdentity() ?? true);
        var gi = identity && (parameters.CurveGreen?.IsIdentity() ?? true);
        var bi = identity && (parameters.CurveBlue?.IsIdentity() ?? true);
        var workers = Math.Min(Environment.ProcessorCount, Math.Max(1, raw ? (count + 32767) / 32768 : count / 8192));
        Parallel.For(0, workers, worker =>
        {
            for (var p = (int)((long)count * worker / workers); p < (long)count * (worker + 1) / workers; p++)
            {
                var o = p * layout.Channels;
                var r = values[o + layout.Red] / 65535d;
                var g = values[o + layout.Green] / 65535d;
                var b = values[o + layout.Blue] / 65535d;
                var ir = input.Row0(r, g, b); var ig = input.Row1(r, g, b); var ib = input.Row2(r, g, b);
                var adjusted = false;
                if (plan != null)
                {
                    if (raw)
                    {
                        var cr = basis.Row0(r, g, b); var cg = basis.Row1(r, g, b); var cb = basis.Row2(r, g, b);
                        if (plan.ApplyColor(p, ref cr, ref cg, ref cb, normalized.Fold))
                        {
                            ir = inset.Row0(cr, cg, cb); ig = inset.Row1(cr, cg, cb); ib = inset.Row2(cr, cg, cb);
                            adjusted = true;
                        }
                    }
                    else
                    {
                        adjusted = plan.ApplyColor(p, ref ir, ref ig, ref ib, normalized.Fold);
                        if (adjusted) { ir = Math.Max(0, ir); ig = Math.Max(0, ig); ib = Math.Max(0, ib); }
                    }
                }
                double tr, tg, tb;
                if (!adjusted)
                {
                    tr = ToneLutApplicator.Interpolate(luts.Red, ir);
                    tg = ToneLutApplicator.Interpolate(luts.Green, ig);
                    tb = ToneLutApplicator.Interpolate(luts.Blue, ib);
                }
                else if (raw)
                {
                    tr = Tone(ir, parameters.CurveRed, ri); tg = Tone(ig, parameters.CurveGreen, gi); tb = Tone(ib, parameters.CurveBlue, bi);
                }
                else
                {
                    tr = ToneLut.Evaluate(standard, ir, standard.CurveRed);
                    tg = ToneLut.Evaluate(standard, ig, standard.CurveGreen);
                    tb = ToneLut.Evaluate(standard, ib, standard.CurveBlue);
                }
                values[o + layout.Red] = Quantum(raw ? ToneLut.SrgbEncode(Math.Clamp(outset.Row0(tr, tg, tb), 0, 1)) : tr);
                values[o + layout.Green] = Quantum(raw ? ToneLut.SrgbEncode(Math.Clamp(outset.Row1(tr, tg, tb), 0, 1)) : tg);
                values[o + layout.Blue] = Quantum(raw ? ToneLut.SrgbEncode(Math.Clamp(outset.Row2(tr, tg, tb), 0, 1)) : tb);
            }
        });
        pixels.SetArea(0, 0, image.Width, image.Height, values);
        double Tone(double value, CurveData? curve, bool id) => AgxToneEngine.EvaluateToneExtendedUnchecked(
            value, parameters, exposure, log2Fold, slope, toe, shoulder, curve, toeScale, shoulderScale, id);
    }

    private static ushort Quantum(double value) => (ushort)Math.Round(Math.Clamp(value, 0, 1) * 65535,
        MidpointRounding.AwayFromZero);
}
