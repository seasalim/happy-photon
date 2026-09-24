using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

// Mirrors RenderLocals.ApplyColor, substituting only the geometric term. Range classification
// stays on the original, unfurled, post-WB pre-tone basis and is shared by all brush locals.
internal sealed class LocalsBrushPlan
{
    private readonly LocalsBrushEvaluation evaluation;
    private readonly LocalAdjustment[] edits;
    private readonly AgxCrossing.Matrix3x3[] colors;
    private readonly bool monochrome;
    internal double IndexMilliseconds => evaluation.BuildMilliseconds;
    internal string BuildReport => evaluation.Report;

    internal LocalsBrushPlan(BrushDocument[] documents, EditSettings settings, BaseImageInfo info, int width, int height)
    {
        monochrome = info.IsMonochrome;
        evaluation = new(documents, width, height);
        edits = settings.Locals!.ToArray();
        var (kelvin, tint) = settings.Wb.Mode switch
        {
            WbMode.Custom or WbMode.Preset => (settings.Wb.Kelvin!.Value, settings.Wb.Tint!.Value),
            WbMode.Picked => WhiteBalanceModel.EstimateFromGains(settings.Wb.Gains!),
            _ => (info.AsShotKelvin, info.AsShotTint)
        };
        colors = edits.Select(local =>
        {
            var wb = monochrome ? ChromaticAdaptation.Identity() : WhiteBalanceModel.CreateMatrix(
                1e6 / (1e6 / kelvin - local.Temperature), tint + local.Tint, kelvin, tint);
            var saturation = new double[3, 3];
            double[] luminance = [.2627002120112671, .6779980715188708, .0593017164698620];
            var scale = monochrome ? 1 : 1 + local.Saturation / 100;
            for (var r = 0; r < 3; r++) for (var c = 0; c < 3; c++)
                saturation[r, c] = (r == c ? scale : 0) + (1 - scale) * luminance[c];
            var matrix = ChromaticAdaptation.Multiply(saturation, wb);
            for (var r = 0; r < 3; r++) for (var c = 0; c < 3; c++) matrix[r, c] *= Math.Pow(2, local.Exposure);
            return new AgxCrossing.Matrix3x3(matrix);
        }).ToArray();
    }

    internal bool ApplyColor(int pixel, ref double r, ref double g, ref double b, double fold)
    {
        var original = (r, g, b);
        OklabColor.Classification? lab = null;
        double? hue = null, chroma = null;
        for (var i = 0; i < evaluation.Count; i++)
        {
            var weight = evaluation.Weight(i, pixel);
            if (weight == 0) continue;
            if (edits[i].Luminance is { Enabled: true } range)
            {
                lab ??= OklabColor.Classify(original.r * fold, original.g * fold, original.b * fold);
                weight *= LuminanceWindow.Weight(range, lab.Value.L);
                if (weight == 0) continue;
            }
            if (!monochrome && edits[i].Hue is { Enabled: true } h)
            {
                lab ??= OklabColor.Classify(original.r * fold, original.g * fold, original.b * fold);
                hue ??= lab.Value.Hue; chroma ??= lab.Value.Chroma;
                weight *= HueWindow.Weight(h, hue.Value, chroma.Value);
                if (weight == 0) continue;
            }
            var matrix = colors[i];
            (r, g, b) = (r + weight * (matrix.Row0(r, g, b) - r),
                g + weight * (matrix.Row1(r, g, b) - g), b + weight * (matrix.Row2(r, g, b) - b));
        }
        return original != (r, g, b);
    }

    internal static double RangeWeight(LocalAdjustment local, OklabColor.Classification lab, bool monochrome = false) =>
        (local.Luminance is { Enabled: true } l ? LuminanceWindow.Weight(l, lab.L) : 1) *
        (!monochrome && local.Hue is { Enabled: true } h ? HueWindow.Weight(h, lab.Hue, lab.Chroma) : 1);
}
