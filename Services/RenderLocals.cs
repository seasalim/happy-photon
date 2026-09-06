using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal sealed class RenderLocals
{
    private readonly Term[] _terms;
    private readonly AgxCrossing.Matrix3x3?[]? _colors;
    private readonly int _width;
    private readonly record struct Term(double X, double Y, double Origin, double Gain,
        bool Radial = false, double AcrossX = 0, double AcrossY = 0, double AcrossOrigin = 0,
        double Feather = 0, bool Outside = false);

    internal bool HasColor => _colors != null;

    private RenderLocals(Term[] terms, int width, AgxCrossing.Matrix3x3?[]? colors) =>
        (_terms, _width, _colors) = (terms, width, colors);

    internal static RenderLocals? Create(EditSettings settings, RenderGeometryTrace frame,
        int width, int height, LocalsFrame? frameOverride = null, BaseImageInfo? info = null)
    {
        bool ColorActive(LocalAdjustment local) => info?.IsMonochrome != true &&
            (local.Temperature != 0 || local.Tint != 0 || local.Saturation != 0);
        var active = settings.Locals?.Where(local => local.Enabled && (local.Exposure != 0 || ColorActive(local))).ToArray();
        if (active is not { Length: > 0 }) return null;
        var correctedWidth = frameOverride?.Width ?? frame.CorrectedFrameWidth;
        var correctedHeight = frameOverride?.Height ?? frame.CorrectedFrameHeight;
        var cropX = frameOverride?.CropX * correctedWidth ?? frame.CropX;
        var cropY = frameOverride?.CropY * correctedHeight ?? frame.CropY;
        var croppedWidth = frameOverride?.CropWidth * correctedWidth ?? frame.Width;
        var croppedHeight = frameOverride?.CropHeight * correctedHeight ?? frame.Height;
        var edge = Math.Max(correctedWidth, correctedHeight);
        var (kelvin, tint) = settings.Wb.Mode switch
        {
            WbMode.Custom or WbMode.Preset => (settings.Wb.Kelvin!.Value, settings.Wb.Tint!.Value),
            WbMode.Picked => WhiteBalanceModel.EstimateFromGains(settings.Wb.Gains!),
            _ => (info?.AsShotKelvin ?? 6504, info?.AsShotTint ?? 0)
        };
        var colors = active.Any(ColorActive) ? active.Select(local => ColorActive(local)
            ? PrepareColor(local, kelvin, tint) : (AgxCrossing.Matrix3x3?)null).ToArray() : null;
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
        return new RenderLocals(linears, width, colors);
    }

    private static AgxCrossing.Matrix3x3 PrepareColor(LocalAdjustment local, double kelvin, double tint)
    {
        var wb = WhiteBalanceModel.CreateMatrix(1e6 / (1e6 / kelvin - local.Temperature),
            tint + local.Tint, kelvin, tint);
        var scale = 1 + local.Saturation / 100;
        double[] luminance = [.2627002120112671, .6779980715188708, .0593017164698620];
        var saturation = new double[3, 3];
        for (var row = 0; row < 3; row++) for (var col = 0; col < 3; col++)
            saturation[row, col] = (row == col ? scale : 0) + (1 - scale) * luminance[col];
        var matrix = ChromaticAdaptation.Multiply(saturation, wb);
        var exposure = Math.Pow(2, local.Exposure);
        for (var row = 0; row < 3; row++) for (var col = 0; col < 3; col++) matrix[row, col] *= exposure;
        return new(matrix);
    }

    internal bool ApplyColor(int pixel, ref double r, ref double g, ref double b)
    {
        var x = pixel % _width + .5;
        var y = pixel / _width + .5;
        var original = (r, g, b);
        for (var i = 0; i < _terms.Length; i++)
        {
            var term = _terms[i];
            var t = term.Origin + x * term.X + y * term.Y;
            double weight;
            if (term.Radial)
            {
                var across = term.AcrossOrigin + x * term.AcrossX + y * term.AcrossY;
                var rho = Math.Sqrt(t * t + across * across);
                t = term.Feather == 0 ? (rho < 1 ? 0 : 1) :
                    Math.Clamp((rho - (1 - term.Feather)) / term.Feather, 0, 1);
                weight = 1 - t * t * (3 - 2 * t);
                if (term.Outside) weight = 1 - weight;
            }
            else { t = Math.Clamp(t, 0, 1); weight = 1 - t * t * (3 - 2 * t); }
            if (weight == 0) continue;
            if (_colors?[i] is { } matrix)
                (r, g, b) = (r + weight * (matrix.Row0(r, g, b) - r),
                    g + weight * (matrix.Row1(r, g, b) - g), b + weight * (matrix.Row2(r, g, b) - b));
            else { var gain = 1 + weight * term.Gain; r *= gain; g *= gain; b *= gain; }
        }
        return original != (r, g, b);
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
