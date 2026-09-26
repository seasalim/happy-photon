using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

internal sealed record OpsAmountField(short[] Texture, short[] Clarity)
{
    internal static OpsAmountField? Create(int pixels, IReadOnlyList<LocalAdjustment>? locals,
        double texture, double clarity) => locals?.Any(l => l.Enabled) == true && (texture != 0 || clarity != 0)
            ? new(new short[pixels], new short[pixels]) : null;

    internal void Fill(int pixel, int width, int height, double r, double g, double b,
        IReadOnlyList<LocalAdjustment> locals, double texture = 30, double clarity = 50)
    {
        var basis = OklabColor.Classify(r, g, b);
        double sumTexture = 0, sumClarity = 0;
        foreach (var local in locals)
        {
            if (!local.Enabled) continue;
            var weight = Weight(local, pixel, width, height, basis);
            sumTexture += weight * texture; sumClarity += weight * clarity;
        }
        Texture[pixel] = (short)Math.Round(Math.Clamp(sumTexture, -200, 200) * 100, MidpointRounding.AwayFromZero);
        Clarity[pixel] = (short)Math.Round(Math.Clamp(sumClarity, -200, 200) * 100, MidpointRounding.AwayFromZero);
    }

    internal static double Weight(LocalAdjustment local, int pixel, int width, int height, OklabColor.Classification basis)
    {
        var edge = Math.Max(width, height);
        var x = (pixel % width + .5 - local.Cu * width) / edge;
        var y = (pixel / width + .5 - local.Cv * height) / edge;
        var angle = local.Angle * Math.PI / 180; var cos = Math.Cos(angle); var sin = Math.Sin(angle);
        var along = x * cos + y * sin; var across = -x * sin + y * cos;
        double t;
        if (local.IsRadial)
        {
            var rho = Math.Sqrt(along * along / (local.Rx * local.Rx) + across * across / (local.Ry * local.Ry));
            t = local.Feather == 0 ? (rho < 1 ? 0 : 1) : Math.Clamp((rho - 1 + local.Feather) / local.Feather, 0, 1);
        }
        else if (local.IsBrush) throw new NotSupportedException("OPS-WP1 freezes the LH8 radial workload only.");
        else t = Math.Clamp(along / local.Feather + .5, 0, 1);
        var weight = 1 - t * t * (3 - 2 * t);
        if (local.IsRadial && local.Outside) weight = 1 - weight;
        if (local.Luminance?.IsEffective == true) weight *= LuminanceWindow.Weight(local.Luminance, basis.L);
        if (local.Hue?.Enabled == true) weight *= HueWindow.Weight(local.Hue, basis.Hue, basis.Chroma);
        return weight;
    }
}
