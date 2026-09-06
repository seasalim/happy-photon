using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

public sealed partial class LocalsFusedBaselineTests
{
    // Closed-form weight field at preview resolution against the full-resolution field binned
    // into the preview grid: separates mask sampling from exposure non-commutation in G5.
    private static (double Mean, double P99, double Gross) WeightFieldDisagreement(BaseImage full,
        BaseImage preview, EditSettings settings)
    {
        using var previewGeometry = RenderGeometry.Apply(preview.Pixels, settings, out var previewTrace);
        using var fullGeometry = RenderGeometry.Apply(full.Pixels, settings, out var fullTrace);
        var w = (int)previewGeometry.Width; var h = (int)previewGeometry.Height;
        var fw = (int)fullGeometry.Width; var fh = (int)fullGeometry.Height;
        var gain = Math.Pow(2, settings.Locals![0].Exposure) - 1;
        var previewPlan = RenderLocals.Create(settings, previewTrace, w, h)!;
        var fullPlan = RenderLocals.Create(settings, fullTrace, fw, fh)!;
        var sum = new double[w * h]; var count = new int[w * h];
        for (var y = 0; y < fh; y++)
        {
            var by = Math.Min(h - 1, (int)((y + .5) * h / fh));
            for (var x = 0; x < fw; x++)
            {
                var bx = Math.Min(w - 1, (int)((x + .5) * w / fw));
                sum[by * w + bx] += (fullPlan.Gain(y * fw + x) - 1) / gain;
                count[by * w + bx]++;
            }
        }
        var errors = new double[w * h];
        for (var i = 0; i < errors.Length; i++)
            errors[i] = Math.Abs((previewPlan.Gain(i) - 1) / gain - sum[i] / Math.Max(1, count[i]));
        var sorted = errors.Order().ToArray();
        return (errors.Average(), sorted[(int)(sorted.Length * .99)], errors.Count(e => e > .05) / (double)errors.Length);
    }
}
