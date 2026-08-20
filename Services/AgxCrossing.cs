using System.Runtime.CompilerServices;
using ImageMagick;

namespace HappyPhoton.Services;

internal readonly record struct AgxRgb(double Red, double Green, double Blue);

internal sealed class AgxCrossing
{
    private const double Q16ToUnit = 1.0 / ushort.MaxValue;

    private readonly AgxToneParameters _parameters;
    private readonly ToneLuts _luts;
    private readonly Matrix3x3 _input;
    private readonly Matrix3x3 _outset = new(AgxToneEngine.OutsetMatrix);
    private readonly double _log2Fold;
    private readonly double _slope;
    private readonly double _toePower;
    private readonly double _shoulderPower;

    private readonly DcpHueSatMap? _hueSatMap;

    internal AgxCrossing(
        AgxToneParameters parameters,
        double[,]? whiteBalanceMatrix = null,
        DcpHueSatMap? hueSatMap = null)
    {
        ArgumentNullException.ThrowIfNull(parameters.Curve);
        _parameters = parameters with
        {
            Curve = parameters.Curve.Clone(),
            CurveRed = parameters.CurveRed?.Clone(),
            CurveGreen = parameters.CurveGreen?.Clone(),
            CurveBlue = parameters.CurveBlue?.Clone()
        };

        if (whiteBalanceMatrix == null)
        {
            _input = new Matrix3x3(AgxToneEngine.InsetMatrix);
            Fold = 1;
        }
        else
        {
            var composed = ChromaticAdaptation.Multiply(
                AgxToneEngine.InsetMatrix,
                whiteBalanceMatrix);
            var normalized = ChromaticAdaptation.NormalizeForRender(composed);
            _input = new Matrix3x3(normalized.Matrix);
            Fold = normalized.Fold;
        }

        _luts = AgxToneLut.ComposeCached(_parameters, Fold);
        _log2Fold = Math.Log2(Fold);
        _slope = AgxToneEngine.Slope(_parameters.Contrast);
        _toePower = AgxToneEngine.ToePower(_parameters.Shadows);
        _shoulderPower = AgxToneEngine.ShoulderPower(_parameters.Highlights);
    }

    internal AgxCrossing(
        AgxToneParameters parameters,
        double[,]? whiteBalanceMatrix,
        RenderExecutionOptions execution,
        DcpHueSatMap? hueSatMap = null)
    {
        ArgumentNullException.ThrowIfNull(parameters.Curve);
        _parameters = parameters with { Curve = parameters.Curve.Clone() };
        _hueSatMap = hueSatMap;

        if (whiteBalanceMatrix == null)
        {
            _input = new Matrix3x3(AgxToneEngine.InsetMatrix);
            Fold = 1;
        }
        else
        {
            var composed = ChromaticAdaptation.Multiply(
                AgxToneEngine.InsetMatrix,
                whiteBalanceMatrix);
            var normalized = ChromaticAdaptation.NormalizeForRender(composed);
            _input = new Matrix3x3(normalized.Matrix);
            Fold = normalized.Fold;
        }

        _luts = AgxToneLut.ComposeCached(_parameters, Fold, execution);
        _log2Fold = Math.Log2(Fold);
        _slope = AgxToneEngine.Slope(_parameters.Contrast);
        _toePower = AgxToneEngine.ToePower(_parameters.Shadows);
        _shoulderPower = AgxToneEngine.ShoulderPower(_parameters.Highlights);
    }

    internal double Fold { get; }

    internal void Apply(ushort[] rgb) => Apply(rgb, rgb);

    internal void Apply(MagickImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var pixels = image.GetPixels();
        var values = pixels.GetArea(0, 0, image.Width, image.Height) ??
            throw new InvalidOperationException("Unable to access Q16 pixels.");
        var layout = RenderKernelSupport.GetLayout(pixels);
        var pixelCount = checked((int)(image.Width * image.Height));
        if (_hueSatMap != null)
        {
            // The DCP HueSat pass is fused onto the crossing's working array:
            // scene-linear per §7.4, one whole-frame read and write total.
            DcpHueSatRenderer.ApplyValues(
                values,
                pixelCount,
                layout.Channels,
                layout.Red,
                layout.Green,
                layout.Blue,
                _hueSatMap);
        }
        Apply(
            values,
            pixelCount,
            layout.Channels,
            layout.Red,
            layout.Green,
            layout.Blue);
        pixels.SetArea(0, 0, image.Width, image.Height, values);
    }

    internal void Apply(
        MagickImage image,
        RenderExecutionOptions execution)
    {
        ArgumentNullException.ThrowIfNull(image);
        execution.ThrowIfCancellationRequested();
        using var pixels = image.GetPixels();
        var values = pixels.GetArea(0, 0, image.Width, image.Height) ??
            throw new InvalidOperationException("Unable to access Q16 pixels.");
        execution.ThrowIfCancellationRequested();
        var layout = RenderKernelSupport.GetLayout(pixels);
        var restingPixelCount = checked((int)(image.Width * image.Height));
        if (_hueSatMap != null)
        {
            // Keep resting renders pixel-identical to interactive ones: the
            // fused DCP HueSat pass runs on the same working array here too.
            DcpHueSatRenderer.ApplyValues(
                values,
                restingPixelCount,
                layout.Channels,
                layout.Red,
                layout.Green,
                layout.Blue,
                _hueSatMap);
            execution.ThrowIfCancellationRequested();
        }
        ApplyResting(
            values,
            restingPixelCount,
            layout.Channels,
            layout.Red,
            layout.Green,
            layout.Blue,
            execution);
        execution.ThrowIfCancellationRequested();
        pixels.SetArea(0, 0, image.Width, image.Height, values);
        execution.ThrowIfCancellationRequested();
    }

    internal void Apply(ushort[] source, ushort[] destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (source.Length % 3 != 0)
        {
            throw new ArgumentException(
                "Expected interleaved RGB samples.",
                nameof(source));
        }
        if (destination.Length != source.Length)
        {
            throw new ArgumentException(
                "Destination must match the source length.",
                nameof(destination));
        }

        var pixelCount = source.Length / 3;
        if (ReferenceEquals(source, destination))
        {
            Apply(source, pixelCount, 3, 0, 1, 2);
            return;
        }

        source.CopyTo(destination, 0);
        Apply(destination, pixelCount, 3, 0, 1, 2);
    }

    private void Apply(
        ushort[] values,
        int pixelCount,
        int channels,
        int redChannel,
        int greenChannel,
        int blueChannel)
    {
        var workers = Math.Min(
            Environment.ProcessorCount,
            Math.Max(1, (pixelCount + 32_767) / 32_768));
        var input = _input;
        var outset = _outset;
        var luts = _luts;

        Parallel.For(0, workers, worker =>
        {
            var start = pixelCount * worker / workers;
            var end = pixelCount * (worker + 1) / workers;
            for (var pixel = start; pixel < end; pixel++)
            {
                var offset = pixel * channels;
                var red = values[offset + redChannel] * Q16ToUnit;
                var green = values[offset + greenChannel] * Q16ToUnit;
                var blue = values[offset + blueChannel] * Q16ToUnit;

                var insetRed = Clamp01(input.Row0(red, green, blue));
                var insetGreen = Clamp01(input.Row1(red, green, blue));
                var insetBlue = Clamp01(input.Row2(red, green, blue));
                var toneRed = AgxToneLut.InterpolateUnchecked(luts.Red, insetRed);
                var toneGreen = AgxToneLut.InterpolateUnchecked(luts.Green, insetGreen);
                var toneBlue = AgxToneLut.InterpolateUnchecked(luts.Blue, insetBlue);

                values[offset + redChannel] = EncodeQ16(
                    outset.Row0(toneRed, toneGreen, toneBlue));
                values[offset + greenChannel] = EncodeQ16(
                    outset.Row1(toneRed, toneGreen, toneBlue));
                values[offset + blueChannel] = EncodeQ16(
                    outset.Row2(toneRed, toneGreen, toneBlue));
            }
        });
    }

    private void ApplyResting(
        ushort[] values,
        int pixelCount,
        int channels,
        int redChannel,
        int greenChannel,
        int blueChannel,
        RenderExecutionOptions execution)
    {
        var workers = execution.CapWorkers(Math.Min(
            Environment.ProcessorCount,
            Math.Max(1, (pixelCount + 32_767) / 32_768)));
        var input = _input;
        var outset = _outset;
        var luts = _luts;

        Parallel.For(0, workers, execution.ParallelOptions, worker =>
        {
            var start = pixelCount * worker / workers;
            var end = pixelCount * (worker + 1) / workers;
            for (var pixel = start; pixel < end; pixel++)
            {
                var offset = pixel * channels;
                var red = values[offset + redChannel] * Q16ToUnit;
                var green = values[offset + greenChannel] * Q16ToUnit;
                var blue = values[offset + blueChannel] * Q16ToUnit;

                var insetRed = Clamp01(input.Row0(red, green, blue));
                var insetGreen = Clamp01(input.Row1(red, green, blue));
                var insetBlue = Clamp01(input.Row2(red, green, blue));
                var toneRed = AgxToneLut.InterpolateUnchecked(
                    luts.Red,
                    insetRed);
                var toneGreen = AgxToneLut.InterpolateUnchecked(
                    luts.Green,
                    insetGreen);
                var toneBlue = AgxToneLut.InterpolateUnchecked(
                    luts.Blue,
                    insetBlue);

                values[offset + redChannel] = EncodeQ16(
                    outset.Row0(toneRed, toneGreen, toneBlue));
                values[offset + greenChannel] = EncodeQ16(
                    outset.Row1(toneRed, toneGreen, toneBlue));
                values[offset + blueChannel] = EncodeQ16(
                    outset.Row2(toneRed, toneGreen, toneBlue));
            }
        });
    }

    internal AgxRgb TransformAnalytic(AgxRgb input)
    {
        var inset = TransformInput(input, _input);
        var exposureGain = Math.Pow(
            2,
            _parameters.ExposureEv + _parameters.SourceExposureEv);
        return TransformAnalytic(inset, exposureGain);
    }

    internal AgxRgb TransformAnalyticAtExposure(
        AgxRgb input,
        double exposureEv)
    {
        if (!double.IsFinite(exposureEv))
        {
            throw new ArgumentOutOfRangeException(nameof(exposureEv));
        }

        return TransformAnalytic(
            TransformInput(input, _input),
            Math.Pow(2, exposureEv));
    }

    private AgxRgb TransformAnalytic(AgxRgb inset, double exposureGain)
    {
        var tone = new AgxRgb(
            EvaluateTone(inset.Red, exposureGain, _parameters.CurveRed),
            EvaluateTone(inset.Green, exposureGain, _parameters.CurveGreen),
            EvaluateTone(inset.Blue, exposureGain, _parameters.CurveBlue));
        return TransformOutput(tone, _outset);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double EvaluateTone(
        double value,
        double exposureGain,
        HappyPhoton.Models.CurveData? channelCurve) =>
        AgxToneEngine.EvaluateToneUnchecked(
            value,
            _parameters,
            exposureGain,
            _log2Fold,
            _slope,
            _toePower,
            _shoulderPower,
            channelCurve);

    internal AgxRgb TransformInterpolated(AgxRgb input)
    {
        var inset = TransformInput(input, _input);
        var tone = new AgxRgb(
            AgxToneLut.InterpolateUnchecked(_luts.Red, inset.Red),
            AgxToneLut.InterpolateUnchecked(_luts.Green, inset.Green),
            AgxToneLut.InterpolateUnchecked(_luts.Blue, inset.Blue));
        return TransformOutput(tone, _outset);
    }

    internal static AgxRgb TransformAnalytic(
        AgxRgb input,
        AgxToneParameters parameters)
    {
        AgxToneEngine.Validate(parameters, fold: 1);
        var inset = TransformInput(
            input,
            new Matrix3x3(AgxToneEngine.InsetMatrix));
        var tone = new AgxRgb(
            AgxToneEngine.EvaluateTone(
                inset.Red, parameters, fold: 1, parameters.CurveRed),
            AgxToneEngine.EvaluateTone(
                inset.Green, parameters, fold: 1, parameters.CurveGreen),
            AgxToneEngine.EvaluateTone(
                inset.Blue, parameters, fold: 1, parameters.CurveBlue));
        return TransformOutput(
            tone,
            new Matrix3x3(AgxToneEngine.OutsetMatrix));
    }

    private static AgxRgb TransformInput(AgxRgb value, Matrix3x3 matrix) =>
        new(
            Clamp01(matrix.Row0(value.Red, value.Green, value.Blue)),
            Clamp01(matrix.Row1(value.Red, value.Green, value.Blue)),
            Clamp01(matrix.Row2(value.Red, value.Green, value.Blue)));

    private static AgxRgb TransformOutput(AgxRgb value, Matrix3x3 matrix) =>
        new(
            Encode(matrix.Row0(value.Red, value.Green, value.Blue)),
            Encode(matrix.Row1(value.Red, value.Green, value.Blue)),
            Encode(matrix.Row2(value.Red, value.Green, value.Blue)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort EncodeQ16(double value) =>
        (ushort)Math.Round(
            Encode(value) * ushort.MaxValue,
            MidpointRounding.AwayFromZero);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Encode(double value) =>
        ToneLut.SrgbEncode(Clamp01(value));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);

    private readonly record struct Matrix3x3(
        double M00,
        double M01,
        double M02,
        double M10,
        double M11,
        double M12,
        double M20,
        double M21,
        double M22)
    {
        internal Matrix3x3(double[,] values) : this(
            values[0, 0], values[0, 1], values[0, 2],
            values[1, 0], values[1, 1], values[1, 2],
            values[2, 0], values[2, 1], values[2, 2])
        {
            if (values.GetLength(0) != 3 || values.GetLength(1) != 3)
            {
                throw new ArgumentException("Expected a 3x3 matrix.", nameof(values));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal double Row0(double red, double green, double blue) =>
            M00 * red + M01 * green + M02 * blue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal double Row1(double red, double green, double blue) =>
            M10 * red + M11 * green + M12 * blue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal double Row2(double red, double green, double blue) =>
            M20 * red + M21 * green + M22 * blue;
    }
}
