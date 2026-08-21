using ImageMagick;

namespace HappyPhoton.Services;

/// <summary>
/// Immutable-by-convention, row-packed source-domain channel flags. Each channel
/// uses one bit per pixel so preview-pair artifacts stay within the decode budget.
/// </summary>
internal sealed class SourceSaturationMask
{
    private const int ChannelCount = 3;
    private readonly byte[] _planes;

    internal int Width { get; }
    internal int Height { get; }
    internal int RowStride { get; }

    internal SourceSaturationMask(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        RowStride = checked((width + 7) / 8);
        _planes = new byte[checked(RowStride * height * ChannelCount)];
    }

    internal byte GetFlags(int x, int y)
    {
        var bit = (byte)(1 << (x & 7));
        var offset = checked(y * RowStride + (x >> 3));
        var planeSize = checked(RowStride * Height);
        byte flags = 0;
        if ((_planes[offset] & bit) != 0) flags |= 1;
        if ((_planes[planeSize + offset] & bit) != 0) flags |= 2;
        if ((_planes[2 * planeSize + offset] & bit) != 0) flags |= 4;
        return flags;
    }

    internal void SetFlags(int x, int y, byte flags)
    {
        if (flags == 0) return;
        var bit = (byte)(1 << (x & 7));
        var offset = checked(y * RowStride + (x >> 3));
        var planeSize = checked(RowStride * Height);
        if ((flags & 1) != 0) _planes[offset] |= bit;
        if ((flags & 2) != 0) _planes[planeSize + offset] |= bit;
        if ((flags & 4) != 0) _planes[2 * planeSize + offset] |= bit;
    }

    internal static bool IsNearEndpoint(uint sample, uint encodedMaximum)
    {
        ArgumentOutOfRangeException.ThrowIfZero(encodedMaximum);
        if (sample > encodedMaximum)
        {
            throw new ArgumentOutOfRangeException(nameof(sample));
        }
        return (ulong)sample * 255 >= (ulong)encodedMaximum * 253;
    }

    internal static SourceSaturationMask CaptureEncoded(
        MagickImage image,
        uint? encodedMaximum,
        int maxDimension,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDimension);
        var sourceWidth = checked((int)image.Width);
        var sourceHeight = checked((int)image.Height);
        var (width, height) = BoundedSize(
            sourceWidth,
            sourceHeight,
            maxDimension);
        var result = new SourceSaturationMask(width, height);
        using var pixels = image.GetPixelsUnsafe();
        var values = pixels.ToShortArray(PixelMapping.RGB) ??
            throw new InvalidOperationException(
                "Unable to read encoded source pixels.");
        var workers = Math.Min(
            Environment.ProcessorCount,
            Math.Max(1, height / 64));

        Parallel.For(
            0,
            workers,
            new ParallelOptions { CancellationToken = cancellationToken },
            worker =>
            {
                var (startRow, endRow) = ChunkRange(height, worker, workers);
                for (var y = startRow; y < endRow; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var sourceStart = CeilingDivide((long)y * sourceHeight, height);
                    var sourceEnd = CeilingDivide((long)(y + 1) * sourceHeight, height);
                    for (var sourceY = sourceStart; sourceY < sourceEnd; sourceY++)
                    {
                        var rowOffset = sourceY * sourceWidth * ChannelCount;
                        for (var sourceX = 0; sourceX < sourceWidth; sourceX++)
                        {
                            var pixel = rowOffset + sourceX * ChannelCount;
                            byte flags = 0;
                            if (IsDecodedNearEndpoint(
                                    values[pixel], encodedMaximum)) flags |= 1;
                            if (IsDecodedNearEndpoint(
                                    values[pixel + 1], encodedMaximum)) flags |= 2;
                            if (IsDecodedNearEndpoint(
                                    values[pixel + 2], encodedMaximum)) flags |= 4;
                            result.SetFlags(
                                (int)((long)sourceX * width / sourceWidth),
                                y,
                                flags);
                        }
                    }
                }
            });
        return result;
    }

    internal SourceSaturationMask OrientAndResize(
        int orientation,
        int targetWidth,
        int targetHeight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(orientation, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(orientation, 8);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetHeight);
        var swapsAxes = orientation is >= 5 and <= 8;
        var orientedWidth = swapsAxes ? Height : Width;
        var orientedHeight = swapsAxes ? Width : Height;
        if (orientation == 1 && targetWidth == Width && targetHeight == Height)
        {
            return this;
        }

        var result = new SourceSaturationMask(targetWidth, targetHeight);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var flags = GetFlags(x, y);
                if (flags == 0) continue;
                var (orientedX, orientedY) = OrientPoint(x, y, orientation);
                MapSetPixel(
                    result,
                    orientedX,
                    orientedY,
                    orientedWidth,
                    orientedHeight,
                    flags);
            }
        }
        return result;
    }

    internal SourceSaturationMask Resize(int width, int height) =>
        OrientAndResize(1, width, height);

    internal void SetMappedPixel(
        int x,
        int y,
        int sourceWidth,
        int sourceHeight,
        byte flags) =>
        MapSetPixel(this, x, y, sourceWidth, sourceHeight, flags);

    private (int X, int Y) OrientPoint(int x, int y, int orientation) =>
        orientation switch
        {
            1 => (x, y),
            2 => (Width - 1 - x, y),
            3 => (Width - 1 - x, Height - 1 - y),
            4 => (x, Height - 1 - y),
            5 => (y, x),
            6 => (Height - 1 - y, x),
            7 => (Height - 1 - y, Width - 1 - x),
            8 => (y, Width - 1 - x),
            _ => throw new ArgumentOutOfRangeException(nameof(orientation))
        };

    private static void MapSetPixel(
        SourceSaturationMask target,
        int x,
        int y,
        int sourceWidth,
        int sourceHeight,
        byte flags)
    {
        var (left, right) = TargetRange(x, sourceWidth, target.Width);
        var (top, bottom) = TargetRange(y, sourceHeight, target.Height);
        for (var targetY = top; targetY < bottom; targetY++)
        {
            for (var targetX = left; targetX < right; targetX++)
            {
                target.SetFlags(targetX, targetY, flags);
            }
        }
    }

    private static (int Start, int End) TargetRange(
        int source,
        int sourceSize,
        int targetSize)
    {
        if (targetSize <= sourceSize)
        {
            var target = Math.Min(
                targetSize - 1,
                (int)((long)source * targetSize / sourceSize));
            return (target, target + 1);
        }
        return (
            CeilingDivide((long)source * targetSize, sourceSize),
            CeilingDivide((long)(source + 1) * targetSize, sourceSize));
    }

    private static bool IsDecodedNearEndpoint(
        ushort value,
        uint? encodedMaximum)
    {
        if (encodedMaximum == null)
        {
            return (ulong)value * 255 >= (ulong)ushort.MaxValue * 253;
        }
        var maximum = encodedMaximum.Value;
        var sample = (uint)(((ulong)value * maximum + ushort.MaxValue / 2u) /
            ushort.MaxValue);
        return IsNearEndpoint(sample, maximum);
    }

    private static (int Width, int Height) BoundedSize(
        int width,
        int height,
        int maxDimension)
    {
        if (width <= maxDimension && height <= maxDimension) return (width, height);
        var scale = maxDimension / (double)Math.Max(width, height);
        return (
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static int CeilingDivide(long numerator, int denominator) =>
        checked((int)((numerator + denominator - 1) / denominator));

    private static (int Start, int End) ChunkRange(
        int rows,
        int worker,
        int workers) =>
        ((int)((long)rows * worker / workers),
            (int)((long)rows * (worker + 1) / workers));
}
