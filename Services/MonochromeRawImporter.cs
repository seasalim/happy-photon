using System.Buffers;
using System.Runtime.InteropServices;
using ImageMagick;

namespace HappyPhoton.Services;

internal static class MonochromeRawImporter
{
    private const int DestinationBufferBudgetBytes = 3 * 1024 * 1024 / 2;
    internal static unsafe ushort[] AreaAverageToMaxDimension(
        ReadOnlySpan<byte> data, int width, int height, int maxDimension,
        CancellationToken cancellationToken,
        out int outputWidth, out int outputHeight)
    {
        Validate(data, width, height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDimension);
        var scale = Math.Min(1, maxDimension / (double)Math.Max(width, height));
        outputWidth = Math.Max(1, (int)Math.Round(width * scale));
        outputHeight = Math.Max(1, (int)Math.Round(height * scale));
        var output = new ushort[checked(outputWidth * outputHeight)];
        var destinationWidth = outputWidth;
        var destinationHeight = outputHeight;
        var xScale = width / (double)destinationWidth;
        var yScale = height / (double)destinationHeight;
        fixed (byte* sourcePointer = data)
        {
            var sourceAddress = (nint)sourcePointer;
            Parallel.For(0, destinationHeight,
                new ParallelOptions { CancellationToken = cancellationToken }, y =>
                {
                    var sourcePixels = new ReadOnlySpan<ushort>(
                        (void*)sourceAddress, checked(width * height));
                    var top = y * yScale;
                    var bottom = (y + 1) * yScale;
                    var firstY = (int)top;
                    var lastY = Math.Min(height - 1, (int)Math.Ceiling(bottom) - 1);
                    for (var x = 0; x < destinationWidth; x++)
                    {
                        var left = x * xScale;
                        var right = (x + 1) * xScale;
                        var firstX = (int)left;
                        var lastX = Math.Min(width - 1, (int)Math.Ceiling(right) - 1);
                        var weighted = 0.0;
                        for (var sourceY = firstY; sourceY <= lastY; sourceY++)
                        {
                            var yWeight = Math.Min(bottom, sourceY + 1) - Math.Max(top, sourceY);
                            var row = sourceY * width;
                            for (var sourceX = firstX; sourceX <= lastX; sourceX++)
                            {
                                var xWeight = Math.Min(right, sourceX + 1) - Math.Max(left, sourceX);
                                weighted += sourcePixels[row + sourceX] * xWeight * yWeight;
                            }
                        }
                        output[y * destinationWidth + x] = (ushort)Math.Clamp(
                            weighted / (xScale * yScale) + 0.5,
                            ushort.MinValue, ushort.MaxValue);
                    }
                });
        }
        return output;
    }

    internal static MagickImage ImportGray16(
        ReadOnlySpan<ushort> data, int width, int height,
        CancellationToken cancellationToken = default) =>
        ImportGray16(MemoryMarshal.AsBytes(data), width, height, cancellationToken);
    internal static MagickImage ImportGray16(
        ReadOnlySpan<byte> data, int width, int height,
        CancellationToken cancellationToken = default)
    {
        Validate(data, width, height);
        var source = MemoryMarshal.Cast<byte, ushort>(data);
        var image = new MagickImage(MagickColors.Black, (uint)width, (uint)height)
        {
            ColorSpace = ColorSpace.RGB
        };
        ushort[]? buffer = null;
        try
        {
            using var pixels = image.GetPixels();
            var layout = RenderKernelSupport.GetLayout(pixels);
            var samplesPerRow = checked(width * layout.Channels);
            var bandHeight = Math.Max(1, Math.Min(height,
                DestinationBufferBudgetBytes / sizeof(ushort) / samplesPerRow));
            buffer = ArrayPool<ushort>.Shared.Rent(checked(samplesPerRow * bandHeight));
            for (var y = 0; y < height; y += bandHeight)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rows = Math.Min(bandHeight, height - y);
                var pixelCount = checked(width * rows);
                for (var pixel = 0; pixel < pixelCount; pixel++)
                {
                    var gray = source[y * width + pixel];
                    var offset = pixel * layout.Channels;
                    buffer[offset + layout.Red] = gray;
                    buffer[offset + layout.Green] = gray;
                    buffer[offset + layout.Blue] = gray;
                }
                pixels.SetArea(0, y, (uint)width, (uint)rows,
                    buffer.AsSpan(0, checked(pixelCount * layout.Channels)));
            }
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
        finally
        {
            if (buffer != null)
                ArrayPool<ushort>.Shared.Return(buffer);
        }
    }
    private static void Validate(ReadOnlySpan<byte> data, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(width), "Monochrome dimensions must be positive.");
        var expectedLength = checked(width * height * sizeof(ushort));
        if (data.Length != expectedLength)
            throw new ArgumentException(
                $"Expected {expectedLength} bytes for a {width}x{height} gray image.",
                nameof(data));
    }
}
