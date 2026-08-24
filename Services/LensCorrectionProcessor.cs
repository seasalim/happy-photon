using System.Buffers;
using System.Runtime.CompilerServices;
using ImageMagick;

namespace HappyPhoton.Services;

internal static class LensCorrectionProcessor
{
    private const int BufferBudgetBytes = 12 * 1024 * 1024;
    internal static Action? SamplingPassStarted { get; set; }

    internal static (int Width, int Height) GetOutputSize(
        int sourceWidth,
        int sourceHeight,
        int orientation,
        int? maxDimension,
        LensPrescription? prescription = null,
        LensCorrectionReferenceFrame? referenceFrame = null)
    {
        int width;
        int height;
        if (referenceFrame is { } frame)
        {
            width = frame.OutputWidth;
            height = frame.OutputHeight;
        }
        else
        {
            var croppedWidth = prescription == null
                ? sourceWidth
                : Math.Max(1, (int)Math.Round(sourceWidth *
                    prescription.OutputWindow.Width /
                    prescription.SourceWindow.Width));
            var croppedHeight = prescription == null
                ? sourceHeight
                : Math.Max(1, (int)Math.Round(sourceHeight *
                    prescription.OutputWindow.Height /
                    prescription.SourceWindow.Height));
            width = orientation is >= 5 and <= 8 ? croppedHeight : croppedWidth;
            height = orientation is >= 5 and <= 8 ? croppedWidth : croppedHeight;
        }
        var referenceWidth = referenceFrame?.SourceWidth ?? sourceWidth;
        var referenceHeight = referenceFrame?.SourceHeight ?? sourceHeight;
        var availableScale = Math.Min(
            sourceWidth / (double)referenceWidth,
            sourceHeight / (double)referenceHeight);
        var availableLongEdge = Math.Max(1,
            (int)Math.Round(Math.Max(width, height) * availableScale));
        var limit = Math.Min(maxDimension ?? int.MaxValue, availableLongEdge);
        if (Math.Max(width, height) <= limit)
            return (width, height);
        var scale = limit / (double)Math.Max(width, height);
        return (Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
    }

    internal static unsafe MagickImage ImportCorrected(
        ReadOnlySpan<byte> sourceBytes,
        int sourceWidth,
        int sourceHeight,
        int outputWidth,
        int outputHeight,
        int orientation,
        CameraRgbCharacterization characterization,
        LensPrescription prescription,
        BaseDecodeSettings settings,
        CancellationToken cancellationToken,
        LensCorrectionReferenceFrame? referenceFrame = null)
    {
        Validate(sourceBytes, sourceWidth, sourceHeight, outputWidth, outputHeight);
        SamplingPassStarted?.Invoke();
        var output = new MagickImage(
            MagickColors.Black,
            (uint)outputWidth,
            (uint)outputHeight)
        {
            ColorSpace = ColorSpace.RGB,
            Depth = 16
        };
        ushort[]? buffer = null;
        try
        {
            using var pixels = output.GetPixels();
            var layout = RenderKernelSupport.GetLayout(pixels);
            var samplesPerRow = checked(outputWidth * layout.Channels);
            var bandHeight = Math.Max(1, Math.Min(outputHeight,
                BufferBudgetBytes / sizeof(ushort) / samplesPerRow));
            buffer = ArrayPool<ushort>.Shared.Rent(checked(samplesPerRow * bandHeight));
            var zoom = FindCoverScale(
                sourceWidth, sourceHeight, orientation,
                prescription, settings, referenceFrame);
            var correction = new LensCorrectionPlan(
                sourceWidth, sourceHeight, outputWidth, outputHeight,
                orientation, prescription, settings, zoom, referenceFrame);
            fixed (byte* sourcePointer = sourceBytes)
            {
                var source = (nint)sourcePointer;
                for (var y = 0; y < outputHeight; y += bandHeight)
                {
                    var rows = Math.Min(bandHeight, outputHeight - y);
                    TransformBand(
                        source, sourceWidth, sourceHeight,
                        buffer, outputWidth, outputHeight, y, rows,
                        layout, characterization, correction, cancellationToken);
                    pixels.SetArea(0, y, (uint)outputWidth, (uint)rows,
                        buffer.AsSpan(0, checked(samplesPerRow * rows)));
                }
            }
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
        finally
        {
            if (buffer != null) ArrayPool<ushort>.Shared.Return(buffer);
        }
    }

    internal static SourceSaturationMask WarpMask(
        SourceSaturationMask source,
        int outputWidth,
        int outputHeight,
        int orientation,
        LensPrescription prescription,
        BaseDecodeSettings settings,
        LensCorrectionReferenceFrame? referenceFrame = null)
    {
        var result = new SourceSaturationMask(outputWidth, outputHeight);
        var zoom = FindCoverScale(
            source.Width, source.Height, orientation,
            prescription, settings, referenceFrame);
        var correction = new LensCorrectionPlan(
            source.Width, source.Height, outputWidth, outputHeight,
            orientation, prescription, settings, zoom, referenceFrame);
        Parallel.For(0, outputHeight, y =>
        {
            for (var x = 0; x < outputWidth; x++)
            {
                byte flags = 0;
                if (correction.HasSharedGeometry)
                {
                    flags = SampleFlags(correction.MapShared(x, y));
                }
                else
                {
                    var logical = correction.GetLogicalPoint(x, y);
                    for (var channel = 0; channel < 3; channel++)
                    {
                        var channelBit = (byte)(1 << channel);
                        flags |= (byte)(SampleFlags(
                            correction.Map(logical, channel)) & channelBit);
                    }
                }
                result.SetFlags(x, y, flags);
            }
        });
        return result;

        byte SampleFlags(LensPoint point)
        {
            var left = Math.Clamp((int)Math.Floor(point.X), 0, source.Width - 1);
            var top = Math.Clamp((int)Math.Floor(point.Y), 0, source.Height - 1);
            var right = Math.Min(source.Width - 1, left + 1);
            var bottom = Math.Min(source.Height - 1, top + 1);
            return (byte)(source.GetFlags(left, top) |
                source.GetFlags(right, top) |
                source.GetFlags(left, bottom) |
                source.GetFlags(right, bottom));
        }
    }

    private static unsafe void TransformBand(
        nint sourceAddress,
        int sourceWidth,
        int sourceHeight,
        ushort[] buffer,
        int outputWidth,
        int outputHeight,
        int bandY,
        int rows,
        RenderKernelSupport.PixelLayout layout,
        CameraRgbCharacterization characterization,
        LensCorrectionPlan correction,
        CancellationToken cancellationToken)
    {
        var source = (ushort*)sourceAddress;
        var matrix = characterization.CameraToRec2020;
        var applyMatrix = characterization.AppliesMatrix;
        var workers = Math.Min(Environment.ProcessorCount, Math.Max(1, rows / 16));
        Parallel.For(0, workers,
            new ParallelOptions { CancellationToken = cancellationToken }, worker =>
            {
                var startY = bandY + rows * worker / workers;
                var endY = bandY + rows * (worker + 1) / workers;
                for (var y = startY; y < endY; y++)
                for (var x = 0; x < outputWidth; x++)
                {
                    LensPoint logical = default;
                    LensPoint greenPostGeometry = default;
                    double camera0;
                    double camera1;
                    double camera2;
                    if (correction.HasSharedGeometry)
                    {
                        LensPoint point;
                        if (correction.HasVignetting)
                        {
                            logical = correction.GetLogicalPoint(x, y);
                            point = correction.MapShared(
                                logical, out greenPostGeometry);
                        }
                        else
                        {
                            point = correction.MapShared(x, y);
                        }
                        SampleBilinearRgb(
                            source, sourceWidth, sourceHeight,
                            point.X, point.Y,
                            out camera0, out camera1, out camera2);
                    }
                    else
                    {
                        logical = correction.GetLogicalPoint(x, y);
                        var point = correction.Map(logical, 0);
                        camera0 = SampleBilinear(
                            source, sourceWidth, sourceHeight, point.X, point.Y, 0);
                        point = correction.Map(
                            logical, 1, out greenPostGeometry);
                        camera1 = SampleBilinear(
                            source, sourceWidth, sourceHeight, point.X, point.Y, 1);
                        point = correction.Map(logical, 2);
                        camera2 = SampleBilinear(
                            source, sourceWidth, sourceHeight, point.X, point.Y, 2);
                    }
                    if (correction.HasVignetting)
                    {
                        var gain = correction.GetVignetteGain(
                            logical, greenPostGeometry);
                        camera0 *= gain;
                        camera1 *= gain;
                        camera2 *= gain;
                    }

                    var destination = ((y - bandY) * outputWidth + x) * layout.Channels;
                    if (applyMatrix)
                    {
                        buffer[destination + layout.Red] = Encode(
                            matrix[0, 0] * camera0 + matrix[0, 1] * camera1 + matrix[0, 2] * camera2);
                        buffer[destination + layout.Green] = Encode(
                            matrix[1, 0] * camera0 + matrix[1, 1] * camera1 + matrix[1, 2] * camera2);
                        buffer[destination + layout.Blue] = Encode(
                            matrix[2, 0] * camera0 + matrix[2, 1] * camera1 + matrix[2, 2] * camera2);
                    }
                    else
                    {
                        buffer[destination + layout.Red] = Encode(camera0);
                        buffer[destination + layout.Green] = Encode(camera1);
                        buffer[destination + layout.Blue] = Encode(camera2);
                    }
                }
            });
    }

    internal static bool CanApply(
        int sourceWidth,
        int sourceHeight,
        int orientation,
        LensPrescription prescription,
        BaseDecodeSettings settings,
        LensCorrectionReferenceFrame? referenceFrame = null)
    {
        return TryFindCoverScale(
            sourceWidth, sourceHeight, orientation,
            prescription, settings, referenceFrame, out _);
    }

    internal static double FindCoverScale(
        int width,
        int height,
        int orientation,
        LensPrescription prescription,
        BaseDecodeSettings settings,
        LensCorrectionReferenceFrame? referenceFrame = null)
    {
        if (TryFindCoverScale(
                width, height, orientation,
                prescription, settings, referenceFrame, out var zoom))
        {
            return zoom;
        }
        throw new InvalidOperationException(
            "The lens prescription cannot cover the corrected frame.");
    }

    private static bool TryFindCoverScale(
        int width,
        int height,
        int orientation,
        LensPrescription prescription,
        BaseDecodeSettings settings,
        LensCorrectionReferenceFrame? referenceFrame,
        out double zoom)
    {
        LensCorrectionReferenceFrame reference;
        if (referenceFrame is { } frame)
        {
            reference = frame;
        }
        else
        {
            var output = GetOutputSize(
                width, height, orientation, maxDimension: null, prescription);
            reference = new LensCorrectionReferenceFrame(
                width, height, output.Width, output.Height);
        }
        zoom = 1;
        if (!settings.Distortion && !settings.ChromaticAberration) return true;
        bool Fits(double zoom)
        {
            var correction = new LensCorrectionPlan(
                reference.SourceWidth, reference.SourceHeight,
                reference.OutputWidth, reference.OutputHeight,
                orientation, prescription, settings, zoom, reference);
            bool PointFits(int x, int y)
            {
                var logical = correction.GetLogicalPoint(x, y);
                for (var channel = 0; channel < 3; channel++)
                {
                    var point = correction.Map(logical, channel);
                    if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
                        point.X < 0 || point.X > reference.SourceWidth - 1 ||
                        point.Y < 0 || point.Y > reference.SourceHeight - 1)
                    {
                        return false;
                    }
                }
                return true;
            }
            for (var x = 0; x < reference.OutputWidth; x++)
                if (!PointFits(x, 0) ||
                    !PointFits(x, reference.OutputHeight - 1)) return false;
            for (var y = 1; y < reference.OutputHeight - 1; y++)
                if (!PointFits(0, y) ||
                    !PointFits(reference.OutputWidth - 1, y)) return false;
            return true;
        }
        if (Fits(1)) return true;
        var low = 1.0;
        var high = 4.0;
        if (!Fits(high)) return false;
        for (var iteration = 0; iteration < 24; iteration++)
        {
            var middle = (low + high) * 0.5;
            if (Fits(middle)) high = middle; else low = middle;
        }
        zoom = high * 1.00001;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe double SampleBilinear(
        ushort* source,
        int width,
        int height,
        double x,
        double y,
        int channel)
    {
        x = Math.Clamp(x, 0, width - 1);
        y = Math.Clamp(y, 0, height - 1);
        var x0 = (int)x;
        var y0 = (int)y;
        var x1 = Math.Min(width - 1, x0 + 1);
        var y1 = Math.Min(height - 1, y0 + 1);
        var fx = x - x0;
        var fy = y - y0;
        var p00 = source[(y0 * width + x0) * 3 + channel];
        var p10 = source[(y0 * width + x1) * 3 + channel];
        var p01 = source[(y1 * width + x0) * 3 + channel];
        var p11 = source[(y1 * width + x1) * 3 + channel];
        return (p00 + (p10 - p00) * fx) * (1 - fy) +
            (p01 + (p11 - p01) * fx) * fy;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void SampleBilinearRgb(
        ushort* source,
        int width,
        int height,
        double x,
        double y,
        out double red,
        out double green,
        out double blue)
    {
        x = Math.Clamp(x, 0, width - 1);
        y = Math.Clamp(y, 0, height - 1);
        var x0 = (int)x;
        var y0 = (int)y;
        var x1 = Math.Min(width - 1, x0 + 1);
        var y1 = Math.Min(height - 1, y0 + 1);
        var fx = x - x0;
        var fy = y - y0;
        var topLeft = source + (y0 * width + x0) * 3;
        var topRight = source + (y0 * width + x1) * 3;
        var bottomLeft = source + (y1 * width + x0) * 3;
        var bottomRight = source + (y1 * width + x1) * 3;
        red = InterpolateChannel(
            topLeft, topRight, bottomLeft, bottomRight, fx, fy, 0);
        green = InterpolateChannel(
            topLeft, topRight, bottomLeft, bottomRight, fx, fy, 1);
        blue = InterpolateChannel(
            topLeft, topRight, bottomLeft, bottomRight, fx, fy, 2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe double InterpolateChannel(
        ushort* topLeft,
        ushort* topRight,
        ushort* bottomLeft,
        ushort* bottomRight,
        double fx,
        double fy,
        int channel) =>
        (topLeft[channel] + (topRight[channel] - topLeft[channel]) * fx) * (1 - fy) +
        (bottomLeft[channel] + (bottomRight[channel] - bottomLeft[channel]) * fx) * fy;

    private static (double U, double V) OrientedToSource(
        double u,
        double v,
        int orientation) => orientation switch
        {
            1 => (u, v), 2 => (1 - u, v), 3 => (1 - u, 1 - v),
            4 => (u, 1 - v), 5 => (v, u), 6 => (v, 1 - u),
            7 => (1 - v, 1 - u), 8 => (1 - v, u),
            _ => throw new ArgumentOutOfRangeException(nameof(orientation))
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort Encode(double value) => value <= 0 ? (ushort)0 :
        value >= ushort.MaxValue ? ushort.MaxValue : (ushort)(value + 0.5);

    private static void Validate(
        ReadOnlySpan<byte> source,
        int sourceWidth,
        int sourceHeight,
        int outputWidth,
        int outputHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || outputWidth <= 0 || outputHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        if (source.Length != checked(sourceWidth * sourceHeight * 3 * sizeof(ushort)))
            throw new ArgumentException("The camera-native RGB buffer has an invalid length.", nameof(source));
    }

}

internal readonly record struct LensCorrectionReferenceFrame(
    int SourceWidth,
    int SourceHeight,
    int OutputWidth,
    int OutputHeight);
