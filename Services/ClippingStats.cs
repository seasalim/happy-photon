using System.Runtime.CompilerServices;
using ImageMagick;

namespace HappyPhoton.Services;

public sealed record ChannelClip(double R, double G, double B)
{
    public static ChannelClip Empty { get; } = new(0, 0, 0);
}

public sealed record ClippingStats(
    ChannelClip High,
    ChannelClip Low,
    double HighAny,
    double LowAll,
    double RawNearClip)
{
    public static ClippingStats Empty { get; } =
        new(ChannelClip.Empty, ChannelClip.Empty, 0, 0, 0);
}

public sealed class ClippingMask : IDisposable
{
    private byte[]? _flags;

    public int Width { get; }
    public int Height { get; }
    public ClippingOverlaySide Sides { get; }

    internal ReadOnlySpan<byte> Flags =>
        _flags ?? throw new ObjectDisposedException(nameof(ClippingMask));

    internal ClippingMask(
        int width,
        int height,
        ClippingOverlaySide sides,
        byte[] flags)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(flags);
        if (flags.Length != checked(width * height))
        {
            throw new ArgumentException(
                "The clipping mask length must match its dimensions.",
                nameof(flags));
        }

        Width = width;
        Height = height;
        Sides = sides;
        _flags = flags;
    }

    public void Dispose() => Interlocked.Exchange(ref _flags, null);
}

internal readonly record struct ClippingAnalysis(
    ClippingStats Stats,
    ClippingMask? OverlayMask);

internal sealed record SceneHighlightAnalysis(
    ChannelClip High,
    double HighAny,
    uint Width,
    uint Height,
    bool[]? HighMask);

internal static class ClippingStatsCalculator
{
    private const ushort HighThreshold = 65407;
    private const ushort LowThreshold = 128;
    private const ushort RawNearClipThreshold = 64880;


    // The near-clip ratio depends only on the decoded base pixels, which are
    // immutable once installed; renders clone before mutating. Cache per base
    // so slider ticks skip the full-frame pass.
    private static readonly ConditionalWeakTable<BaseImage, object>
        RawNearClipCache = new();

    public static double CalculateRawNearClip(BaseImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!image.Info.IsRawSource)
        {
            return 0;
        }

        return (double)RawNearClipCache.GetValue(
            image,
            static current => ComputeRawNearClip(current));
    }

    private static object ComputeRawNearClip(BaseImage image)
    {
        var samples = GetRgbSamples(image.Pixels);
        var pixels = samples.Length / 3;
        long clipped = 0;
        var matrix = RgbColorSpaceMatrices.LinearRec2020ToLinearSrgb;
        var threshold = RawNearClipThreshold / (double)ushort.MaxValue;
        var workers = WorkerCount(pixels);
        Parallel.For(0, workers, worker =>
        {
            var (start, end) = ChunkRange(pixels, worker, workers);
            long localClipped = 0;
            for (var pixel = start; pixel < end; pixel++)
            {
                var i = pixel * 3;
                var red = samples[i] / (double)ushort.MaxValue;
                var green = samples[i + 1] / (double)ushort.MaxValue;
                var blue = samples[i + 2] / (double)ushort.MaxValue;
                var displayRed = matrix[0, 0] * red +
                    matrix[0, 1] * green + matrix[0, 2] * blue;
                var displayGreen = matrix[1, 0] * red +
                    matrix[1, 1] * green + matrix[1, 2] * blue;
                var displayBlue = matrix[2, 0] * red +
                    matrix[2, 1] * green + matrix[2, 2] * blue;
                if (displayRed >= threshold ||
                    displayGreen >= threshold ||
                    displayBlue >= threshold)
                {
                    localClipped++;
                }
            }
            Interlocked.Add(ref clipped, localClipped);
        });

        return pixels == 0 ? 0d : (double)clipped / pixels;
    }

    private static int WorkerCount(int pixelCount) =>
        Math.Min(Environment.ProcessorCount, Math.Max(1, pixelCount / 8192));

    private static (int Start, int End) ChunkRange(
        int pixelCount,
        int worker,
        int workers) =>
        ((int)((long)pixelCount * worker / workers),
            (int)((long)pixelCount * (worker + 1) / workers));

    public static ClippingAnalysis Analyze(
        MagickImage image,
        double rawNearClip,
        bool createOverlay,
        SceneHighlightAnalysis? sceneHighlights = null,
        ClippingOverlaySide overlaySides = ClippingOverlaySide.Both)
    {
        ArgumentNullException.ThrowIfNull(image);
        var samples = GetRgbSamples(image);
        var pixels = samples.Length / 3;
        if (pixels == 0)
        {
            return new ClippingAnalysis(
                ClippingStats.Empty with { RawNearClip = rawNearClip },
                null);
        }

        var flags = createOverlay && overlaySides != ClippingOverlaySide.None
            ? new byte[pixels]
            : null;
        long highR = 0, highG = 0, highB = 0;
        long lowR = 0, lowG = 0, lowB = 0;
        long highAny = 0, lowAll = 0;
        var imageWidth = checked((int)image.Width);
        var imageHeight = checked((int)image.Height);
        var scene = sceneHighlights;
        var sceneHasMask = scene?.HighMask != null;
        var sceneDimensionsMatch = sceneHasMask &&
            scene!.Width == image.Width &&
            scene.Height == image.Height;
        var sceneX = sceneHasMask && !sceneDimensionsMatch
            ? BuildCoordinateMap(imageWidth, checked((int)scene!.Width))
            : null;
        var sceneY = sceneHasMask && !sceneDimensionsMatch
            ? BuildCoordinateMap(imageHeight, checked((int)scene!.Height))
            : null;

        var workers = WorkerCount(pixels);
        Parallel.For(0, workers, worker =>
        {
            var (start, end) = ChunkRange(pixels, worker, workers);
            long localHighR = 0, localHighG = 0, localHighB = 0;
            long localLowR = 0, localLowG = 0, localLowB = 0;
            long localHighAny = 0, localLowAll = 0;
            var imageX = start % imageWidth;
            var imageY = start / imageWidth;
            for (var pixel = start; pixel < end; pixel++)
            {
                var sample = pixel * 3;
                var r = samples[sample];
                var g = samples[sample + 1];
                var b = samples[sample + 2];
                var rHigh = scene == null && r >= HighThreshold;
                var gHigh = scene == null && g >= HighThreshold;
                var bHigh = scene == null && b >= HighThreshold;
                var anyHigh = scene == null
                    ? rHigh || gHigh || bHigh
                    : !sceneHasMask
                        ? false
                    : sceneDimensionsMatch
                        ? scene.HighMask![pixel]
                        : scene.HighMask![
                            sceneY![imageY] * checked((int)scene.Width) +
                            sceneX![imageX]];
                var rLow = r <= LowThreshold;
                var gLow = g <= LowThreshold;
                var bLow = b <= LowThreshold;

                if (rHigh) localHighR++;
                if (gHigh) localHighG++;
                if (bHigh) localHighB++;
                if (rLow) localLowR++;
                if (gLow) localLowG++;
                if (bLow) localLowB++;

                if (anyHigh)
                {
                    localHighAny++;
                    if (flags != null && overlaySides.HasFlag(
                            ClippingOverlaySide.SceneHighlights))
                    {
                        flags[pixel] = (byte)ClippingOverlaySide.SceneHighlights;
                    }
                }
                else if (rLow && gLow && bLow)
                {
                    localLowAll++;
                    if (flags != null && overlaySides.HasFlag(
                            ClippingOverlaySide.DisplayFloor))
                    {
                        flags[pixel] = (byte)ClippingOverlaySide.DisplayFloor;
                    }
                }
                if (sceneHasMask && ++imageX == imageWidth)
                {
                    imageX = 0;
                    imageY++;
                }
            }
            Interlocked.Add(ref highR, localHighR);
            Interlocked.Add(ref highG, localHighG);
            Interlocked.Add(ref highB, localHighB);
            Interlocked.Add(ref lowR, localLowR);
            Interlocked.Add(ref lowG, localLowG);
            Interlocked.Add(ref lowB, localLowB);
            Interlocked.Add(ref highAny, localHighAny);
            Interlocked.Add(ref lowAll, localLowAll);
        });

        var divisor = (double)pixels;
        var stats = new ClippingStats(
            scene?.High ??
                new ChannelClip(highR / divisor, highG / divisor, highB / divisor),
            new ChannelClip(lowR / divisor, lowG / divisor, lowB / divisor),
            scene?.HighAny ?? highAny / divisor,
            lowAll / divisor,
            rawNearClip);
        return new ClippingAnalysis(
            stats,
            flags == null
                ? null
                : new ClippingMask(
                    checked((int)image.Width),
                    checked((int)image.Height),
                    overlaySides,
                    flags));
    }

    internal static SceneHighlightAnalysis AnalyzeSceneHighlights(
        MagickImage image,
        double[,] whiteBalanceMatrix,
        double exposureEv,
        bool createMask)
    {
        ArgumentNullException.ThrowIfNull(image);
        return AnalyzeSceneHighlights(
            GetRgbSamples(image),
            image.Width,
            image.Height,
            whiteBalanceMatrix,
            exposureEv,
            createMask);
    }

    internal static SceneHighlightAnalysis AnalyzeSceneHighlights(
        ushort[] samples,
        uint width,
        uint height,
        double[,] whiteBalanceMatrix,
        double exposureEv,
        bool createMask)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(whiteBalanceMatrix);
        if (whiteBalanceMatrix.GetLength(0) != 3 ||
            whiteBalanceMatrix.GetLength(1) != 3)
        {
            throw new ArgumentException(
                "Expected a 3x3 white-balance matrix.",
                nameof(whiteBalanceMatrix));
        }
        if (!double.IsFinite(exposureEv))
        {
            throw new ArgumentOutOfRangeException(nameof(exposureEv));
        }

        if (samples.Length != checked((int)(width * height * 3)))
        {
            throw new ArgumentException(
                "The RGB samples must match the supplied dimensions.",
                nameof(samples));
        }
        var pixels = samples.Length / 3;
        var mask = createMask ? new bool[pixels] : null;
        long highR = 0, highG = 0, highB = 0, highAny = 0;
        var gain = Math.Pow(2, exposureEv);
        var m00 = whiteBalanceMatrix[0, 0];
        var m01 = whiteBalanceMatrix[0, 1];
        var m02 = whiteBalanceMatrix[0, 2];
        var m10 = whiteBalanceMatrix[1, 0];
        var m11 = whiteBalanceMatrix[1, 1];
        var m12 = whiteBalanceMatrix[1, 2];
        var m20 = whiteBalanceMatrix[2, 0];
        var m21 = whiteBalanceMatrix[2, 1];
        var m22 = whiteBalanceMatrix[2, 2];
        var workers = WorkerCount(pixels);
        Parallel.For(0, workers, worker =>
        {
            var (start, end) = ChunkRange(pixels, worker, workers);
            long localHighR = 0, localHighG = 0, localHighB = 0;
            long localHighAny = 0;
            for (var pixel = start; pixel < end; pixel++)
            {
                var offset = pixel * 3;
                var red = samples[offset] / (double)ushort.MaxValue;
                var green = samples[offset + 1] / (double)ushort.MaxValue;
                var blue = samples[offset + 2] / (double)ushort.MaxValue;
                var rHigh = gain *
                    (m00 * red + m01 * green + m02 * blue) >= 1;
                var gHigh = gain *
                    (m10 * red + m11 * green + m12 * blue) >= 1;
                var bHigh = gain *
                    (m20 * red + m21 * green + m22 * blue) >= 1;
                if (rHigh) localHighR++;
                if (gHigh) localHighG++;
                if (bHigh) localHighB++;
                if (rHigh || gHigh || bHigh)
                {
                    localHighAny++;
                    if (mask != null) mask[pixel] = true;
                }
            }
            Interlocked.Add(ref highR, localHighR);
            Interlocked.Add(ref highG, localHighG);
            Interlocked.Add(ref highB, localHighB);
            Interlocked.Add(ref highAny, localHighAny);
        });

        var divisor = Math.Max(1, pixels);
        return new SceneHighlightAnalysis(
            new ChannelClip(
                highR / (double)divisor,
                highG / (double)divisor,
                highB / (double)divisor),
            highAny / (double)divisor,
            width,
            height,
            mask);
    }

    internal static ushort[] CopyRgbSamples(MagickImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return GetRgbSamples(image);
    }

    private static ushort[] GetRgbSamples(MagickImage image) =>
        image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGB) ??
        throw new InvalidOperationException("Unable to read Q16 RGB pixels.");

    private static int[] BuildCoordinateMap(int destinationSize, int sourceSize)
    {
        var map = new int[destinationSize];
        for (var coordinate = 0; coordinate < destinationSize; coordinate++)
        {
            map[coordinate] = Math.Min(
                sourceSize - 1,
                (int)((coordinate + 0.5) * sourceSize / destinationSize));
        }
        return map;
    }

}
