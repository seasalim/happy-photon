using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal sealed class RenderLocals
{
    private readonly Term[] _terms;
    private readonly LocalBrushEvaluator?[]? _brushes;
    private readonly double _brushX, _brushY, _brushOriginX, _brushOriginY;
    private readonly AgxCrossing.Matrix3x3?[]? _colors;
    private readonly int _width;
    private readonly LuminanceRange?[]? _ranges;
    private readonly HueRange?[]? _hues;
    internal bool HasRange => _ranges != null || _hues != null;
    internal bool NeedsBasis => HasColor || HasRange;
    private readonly record struct Term(double X, double Y, double Origin, double Gain,
        bool Radial = false, double AcrossX = 0, double AcrossY = 0, double AcrossOrigin = 0,
        double Feather = 0, bool Outside = false);

    internal bool HasColor => _colors != null;

    private RenderLocals(Term[] terms, int width, AgxCrossing.Matrix3x3?[]? colors, HueRange?[]? hues,
        LuminanceRange?[]? ranges, LocalBrushEvaluator?[]? brushes,
        double brushX, double brushY, double brushOriginX, double brushOriginY)
    {
        (_terms, _width, _colors, _hues, _ranges) = (terms, width, colors, hues, ranges);
        (_brushes, _brushX, _brushY, _brushOriginX, _brushOriginY) =
            (brushes, brushX, brushY, brushOriginX, brushOriginY);
    }

    internal static RenderLocals? Create(EditSettings settings, RenderGeometryTrace frame,
        int width, int height, LocalsFrame? frameOverride = null, BaseImageInfo? info = null)
    {
        bool ColorActive(LocalAdjustment local) => info?.IsMonochrome != true &&
            (local.Temperature != 0 || local.Tint != 0 || local.Saturation != 0);
        var active = settings.Locals?.Where(local => local.Enabled && (!local.IsBrush || local.Strokes is { Length: > 0 }) && (local.Exposure != 0 || ColorActive(local))).ToArray();
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
            if (local.IsBrush) return new Term(0, 0, 0, Math.Pow(2, local.Exposure) - 1);
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
        var ranges = active.Any(local => local.Luminance?.IsEffective == true)
            ? active.Select(local => local.Luminance?.IsEffective == true ? local.Luminance : null).ToArray() : null;
        var hues = info?.IsMonochrome != true && active.Any(local => local.Hue?.Enabled == true)
            ? active.Select(local => local.Hue?.Enabled == true ? local.Hue : null).ToArray() : null;
        var brushSegments = active.Sum(local => local.IsBrush ? LocalBrushEvaluator.CountSegments(local.Strokes) : 0);
        // Reserve caller setup, then each evaluator's one-cell floor before sharing the remainder.
        var brushExtra = LocalBrushEvaluator.DocumentBudget - 16384 -
            active.Sum(local => local.IsBrush ? LocalBrushEvaluator.MinimumBudget(local.Strokes) : 0);
        var brushes = brushSegments > 0 ? active.Select(local => local.IsBrush
            ? new LocalBrushEvaluator(local.Strokes, correctedWidth, correctedHeight,
                LocalBrushEvaluator.MinimumBudget(local.Strokes) +
                brushExtra * LocalBrushEvaluator.CountSegments(local.Strokes) / brushSegments) : null).ToArray() : null;
        return new RenderLocals(linears, width, colors, hues, ranges, brushes,
            croppedWidth / width / correctedWidth, croppedHeight / height / correctedHeight,
            cropX / correctedWidth, cropY / correctedHeight);
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

    internal bool ApplyColor(int pixel, ref double r, ref double g, ref double b, double fold = 1) =>
        _brushes == null ? ApplyColorCore<GradientWeights>(pixel, ref r, ref g, ref b, fold)
            : ApplyColorCore<BrushWeights>(pixel, ref r, ref g, ref b, fold);

    private interface IWeights { static abstract double Weight(RenderLocals owner, int i, double x, double y); }
    private readonly struct GradientWeights : IWeights
    {
        public static double Weight(RenderLocals owner, int i, double x, double y) => GeometryWeight(owner._terms[i], x, y);
    }
    private readonly struct BrushWeights : IWeights
    {
        public static double Weight(RenderLocals owner, int i, double x, double y) => owner._brushes![i] is { } brush
            ? brush.Weight(owner._brushOriginX + x * owner._brushX, owner._brushOriginY + y * owner._brushY)
            : GeometryWeight(owner._terms[i], x, y);
    }

    private bool ApplyColorCore<T>(int pixel, ref double r, ref double g, ref double b, double fold) where T : struct, IWeights
    {
        var x = pixel % _width + .5;
        var y = pixel / _width + .5;
        var original = (r, g, b);
        double? lightness = null;
        OklabColor.Classification? classification = null;
        double? hue = null, chroma = null;
        for (var i = 0; i < _terms.Length; i++)
        {
            var term = _terms[i];
            var weight = T.Weight(this, i, x, y);
            if (weight == 0) continue;
            if (_ranges?[i] is { } range)
            {
                lightness ??= _hues == null
                    ? OklabColor.ClassifyLightness(original.r * fold, original.g * fold, original.b * fold)
                    : (classification ??= OklabColor.Classify(original.r * fold, original.g * fold, original.b * fold)).L;
                weight *= LuminanceWindow.Weight(range, lightness.Value);
                if (weight == 0) continue;
            }
            if (_hues?[i] is { } hueRange)
            {
                classification ??= OklabColor.Classify(original.r * fold, original.g * fold, original.b * fold);
                hue ??= classification.Value.Hue;
                chroma ??= classification.Value.Chroma;
                weight *= HueWindow.Weight(hueRange, hue.Value, chroma.Value);
                if (weight == 0) continue;
            }
            if (_colors?[i] is { } matrix)
                (r, g, b) = (r + weight * (matrix.Row0(r, g, b) - r),
                    g + weight * (matrix.Row1(r, g, b) - g), b + weight * (matrix.Row2(r, g, b) - b));
            else { var gain = 1 + weight * term.Gain; r *= gain; g *= gain; b *= gain; }
        }
        return original != (r, g, b);
    }

    private static double GeometryWeight(Term term, double x, double y)
    {
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
        return weight;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    internal double Gain(int pixel)
    {
        var gain = 1d;
        var x = pixel % _width + .5;
        var y = pixel / _width + .5;
        if (_brushes != null)
        {
            for (var i = 0; i < _terms.Length; i++) gain *= 1 + BrushWeights.Weight(this, i, x, y) * _terms[i].Gain;
            return gain;
        }
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
