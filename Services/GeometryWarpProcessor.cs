using System.Buffers;
using System.Runtime.CompilerServices;
using ImageMagick;

namespace HappyPhoton.Services;

internal static class GeometryWarpProcessor
{
    private const int BufferBudgetBytes = 12 * 1024 * 1024;
    internal static Action? SamplingPassStarted { get; set; }

    internal static unsafe MagickImage Apply(
        MagickImage source,
        RenderGeometryMap map)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(map);
        SamplingPassStarted?.Invoke();
        var hasAlpha = source.HasAlpha;
        var sourceChannels = hasAlpha ? 4 : 3;
        using var sourcePixels = source.GetPixelsUnsafe();
        var sourceSamples = sourcePixels.ToShortArray(
            hasAlpha ? PixelMapping.RGBA : PixelMapping.RGB) ??
            throw new InvalidOperationException("Unable to read geometry source pixels.");
        var output = new MagickImage(
            hasAlpha ? MagickColors.Transparent : MagickColors.Black,
            (uint)map.OutputWidth,
            (uint)map.OutputHeight)
        {
            ColorSpace = source.ColorSpace,
            Depth = source.Depth
        };
        ushort[]? buffer = null;
        try
        {
            using var outputPixels = output.GetPixels();
            var layout = RenderKernelSupport.GetLayout(outputPixels);
            var alpha = hasAlpha
                ? checked((int)(outputPixels.GetChannelIndex(PixelChannel.Alpha) ??
                    throw new InvalidOperationException(
                        "The geometry output has no alpha channel.")))
                : -1;
            var samplesPerRow = checked(map.OutputWidth * layout.Channels);
            var bandHeight = Math.Max(1, Math.Min(
                map.OutputHeight,
                BufferBudgetBytes / sizeof(ushort) / samplesPerRow));
            buffer = ArrayPool<ushort>.Shared.Rent(
                checked(samplesPerRow * bandHeight));
            fixed (ushort* sourcePointer = sourceSamples)
            {
                for (var y = 0; y < map.OutputHeight; y += bandHeight)
                {
                    var rows = Math.Min(bandHeight, map.OutputHeight - y);
                    TransformBand(
                        sourcePointer,
                        buffer,
                        y,
                        rows,
                        map,
                        layout,
                        sourceChannels,
                        alpha);
                    outputPixels.SetArea(
                        0,
                        y,
                        (uint)map.OutputWidth,
                        (uint)rows,
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

    private static unsafe void TransformBand(
        ushort* source,
        ushort[] buffer,
        int bandY,
        int rows,
        RenderGeometryMap map,
        RenderKernelSupport.PixelLayout layout,
        int sourceChannels,
        int alphaChannel)
    {
        var workers = Math.Min(
            Environment.ProcessorCount,
            Math.Max(1, rows / 16));
        Parallel.For(0, workers, worker =>
        {
            var startY = bandY + rows * worker / workers;
            var endY = bandY + rows * (worker + 1) / workers;
            for (var y = startY; y < endY; y++)
            for (var x = 0; x < map.OutputWidth; x++)
            {
                var point = map.MapInverse(x, y);
                SampleBilinear(
                    source,
                    map.SourceWidth,
                    map.SourceHeight,
                    point.X,
                    point.Y,
                    sourceChannels,
                    out var red,
                    out var green,
                    out var blue,
                    out var alpha);
                var destination =
                    ((y - bandY) * map.OutputWidth + x) * layout.Channels;
                buffer[destination + layout.Red] = Encode(red);
                buffer[destination + layout.Green] = Encode(green);
                buffer[destination + layout.Blue] = Encode(blue);
                if (alphaChannel >= 0)
                    buffer[destination + alphaChannel] = Encode(alpha);
            }
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void SampleBilinear(
        ushort* source,
        int width,
        int height,
        double x,
        double y,
        int channels,
        out double red,
        out double green,
        out double blue,
        out double alpha)
    {
        x = Math.Clamp(x, 0, width - 1);
        y = Math.Clamp(y, 0, height - 1);
        var x0 = (int)x;
        var y0 = (int)y;
        var x1 = Math.Min(width - 1, x0 + 1);
        var y1 = Math.Min(height - 1, y0 + 1);
        var fx = x - x0;
        var fy = y - y0;
        var topLeft = source + (y0 * width + x0) * channels;
        var topRight = source + (y0 * width + x1) * channels;
        var bottomLeft = source + (y1 * width + x0) * channels;
        var bottomRight = source + (y1 * width + x1) * channels;
        red = Interpolate(topLeft, topRight, bottomLeft, bottomRight, fx, fy, 0);
        green = Interpolate(topLeft, topRight, bottomLeft, bottomRight, fx, fy, 1);
        blue = Interpolate(topLeft, topRight, bottomLeft, bottomRight, fx, fy, 2);
        alpha = channels == 4
            ? Interpolate(topLeft, topRight, bottomLeft, bottomRight, fx, fy, 3)
            : ushort.MaxValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe double Interpolate(
        ushort* topLeft,
        ushort* topRight,
        ushort* bottomLeft,
        ushort* bottomRight,
        double fx,
        double fy,
        int channel) =>
        (topLeft[channel] + (topRight[channel] - topLeft[channel]) * fx) *
            (1 - fy) +
        (bottomLeft[channel] + (bottomRight[channel] - bottomLeft[channel]) * fx) *
            fy;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort Encode(double value) =>
        value <= ushort.MinValue ? ushort.MinValue :
        value >= ushort.MaxValue ? ushort.MaxValue :
        (ushort)(value + 0.5);
}
