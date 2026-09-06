using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal sealed class RenderLocals
{
    private readonly Linear[] _linears;
    private readonly int _width;
    private readonly record struct Linear(double X, double Y, double Origin, double Gain);

    private RenderLocals(Linear[] linears, int width) => (_linears, _width) = (linears, width);

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
            var x = croppedWidth / width / edge * cos / local.Feather;
            var y = croppedHeight / height / edge * sin / local.Feather;
            var origin = ((cropX - local.Cu * correctedWidth) * cos +
                (cropY - local.Cv * correctedHeight) * sin) / edge / local.Feather + .5;
            return new Linear(x, y, origin, Math.Pow(2, local.Exposure) - 1);
        }).ToArray();
        return new RenderLocals(linears, width);
    }

    internal double Gain(int pixel)
    {
        var gain = 1d;
        var x = pixel % _width + .5;
        var y = pixel / _width + .5;
        foreach (var linear in _linears)
        {
            var t = Math.Clamp(linear.Origin + x * linear.X + y * linear.Y, 0, 1);
            gain *= 1 + (1 - t * t * (3 - 2 * t)) * linear.Gain;
        }
        return gain;
    }
}
