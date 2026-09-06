using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal sealed class RenderLocals
{
    private readonly Term[] _terms;
    private readonly int _width;
    private readonly record struct Term(double X, double Y, double Origin, double Gain,
        bool Radial = false, double AcrossX = 0, double AcrossY = 0, double AcrossOrigin = 0,
        double Feather = 0, bool Outside = false);

    private RenderLocals(Term[] terms, int width) => (_terms, _width) = (terms, width);

    internal static RenderLocals? Create(EditSettings settings, RenderGeometryTrace frame,
        int width, int height, LocalsFrame? frameOverride = null)
    {
        var active = settings.Locals?.Where(local => local.Enabled && local.Exposure != 0).ToArray();
        if (active is not { Length: > 0 }) return null;
        var correctedWidth = frameOverride?.Width ?? frame.CorrectedFrameWidth;
        var correctedHeight = frameOverride?.Height ?? frame.CorrectedFrameHeight;
        var cropX = frameOverride?.CropX * correctedWidth ?? frame.CropX;
        var cropY = frameOverride?.CropY * correctedHeight ?? frame.CropY;
        var croppedWidth = frameOverride?.CropWidth * correctedWidth ?? frame.Width;
        var croppedHeight = frameOverride?.CropHeight * correctedHeight ?? frame.Height;
        var edge = Math.Max(correctedWidth, correctedHeight);
        var linears = active.Select(local =>
        {
            var cos = Math.Cos(local.Angle * Math.PI / 180);
            var sin = Math.Sin(local.Angle * Math.PI / 180);
            if (local.IsRadial)
                return new Term(croppedWidth / width / edge * cos / local.Rx,
                    croppedHeight / height / edge * sin / local.Rx,
                    ((cropX - local.Cu * correctedWidth) * cos +
                     (cropY - local.Cv * correctedHeight) * sin) / edge / local.Rx,
                    Math.Pow(2, local.Exposure) - 1, true,
                    -croppedWidth / width / edge * sin / local.Ry,
                    croppedHeight / height / edge * cos / local.Ry,
                    (-(cropX - local.Cu * correctedWidth) * sin +
                     (cropY - local.Cv * correctedHeight) * cos) / edge / local.Ry,
                    local.Feather, local.Outside);
            var x = croppedWidth / width / edge * cos / local.Feather;
            var y = croppedHeight / height / edge * sin / local.Feather;
            var origin = ((cropX - local.Cu * correctedWidth) * cos +
                (cropY - local.Cv * correctedHeight) * sin) / edge / local.Feather + .5;
            return new Term(x, y, origin, Math.Pow(2, local.Exposure) - 1);
        }).ToArray();
        return new RenderLocals(linears, width);
    }

    internal double Gain(int pixel)
    {
        var gain = 1d;
        var x = pixel % _width + .5;
        var y = pixel / _width + .5;
        foreach (var linear in _terms)
        {
            if (linear.Radial)
            {
                var a = linear.Origin + x * linear.X + y * linear.Y;
                var b = linear.AcrossOrigin + x * linear.AcrossX + y * linear.AcrossY;
                var rho = Math.Sqrt(a * a + b * b);
                var tRadial = linear.Feather == 0 ? (rho < 1 ? 0 : 1) :
                    Math.Clamp((rho - (1 - linear.Feather)) / linear.Feather, 0, 1);
                var weight = 1 - tRadial * tRadial * (3 - 2 * tRadial);
                gain *= 1 + (linear.Outside ? 1 - weight : weight) * linear.Gain;
                continue;
            }
            var t = Math.Clamp(linear.Origin + x * linear.X + y * linear.Y, 0, 1);
            gain *= 1 + (1 - t * t * (3 - 2 * t)) * linear.Gain;
        }
        return gain;
    }
}
