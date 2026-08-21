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
    bool IsHighAvailable)
{
    public static ClippingStats Empty { get; } =
        new(ChannelClip.Empty, ChannelClip.Empty, 0, 0, false);
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

internal static class ClippingStatsCalculator
{
    private const ushort LowThreshold = 128;

    public static ClippingAnalysis Analyze(
        MagickImage image,
        SourceSaturationProjection? sourceSaturation,
        bool createOverlay,
        ClippingOverlaySide overlaySides = ClippingOverlaySide.Both)
    {
        ArgumentNullException.ThrowIfNull(image);
        var samples = GetRgbSamples(image);
        var pixels = samples.Length / 3;
        if (sourceSaturation != null &&
            (sourceSaturation.Mask.Width != checked((int)image.Width) ||
             sourceSaturation.Mask.Height != checked((int)image.Height)))
        {
            throw new ArgumentException(
                "Source saturation must match the finalized preview dimensions.",
                nameof(sourceSaturation));
        }

        var flags = createOverlay && overlaySides != ClippingOverlaySide.None
            ? new byte[pixels]
            : null;
        long lowR = 0, lowG = 0, lowB = 0, lowAll = 0;
        var workers = WorkerCount(pixels);
        Parallel.For(0, workers, worker =>
        {
            var (start, end) = ChunkRange(pixels, worker, workers);
            long localLowR = 0, localLowG = 0, localLowB = 0, localLowAll = 0;
            for (var pixel = start; pixel < end; pixel++)
            {
                var sample = pixel * 3;
                var rLow = samples[sample] <= LowThreshold;
                var gLow = samples[sample + 1] <= LowThreshold;
                var bLow = samples[sample + 2] <= LowThreshold;
                if (rLow) localLowR++;
                if (gLow) localLowG++;
                if (bLow) localLowB++;
                if (rLow && gLow && bLow)
                {
                    localLowAll++;
                    if (flags != null && overlaySides.HasFlag(
                            ClippingOverlaySide.DisplayFloor))
                    {
                        flags[pixel] |= (byte)ClippingOverlaySide.DisplayFloor;
                    }
                }

                if (flags != null && sourceSaturation != null &&
                    overlaySides.HasFlag(ClippingOverlaySide.Highlights) &&
                    sourceSaturation.Mask.GetFlags(
                        pixel % sourceSaturation.Mask.Width,
                        pixel / sourceSaturation.Mask.Width) != 0)
                {
                    flags[pixel] |= (byte)ClippingOverlaySide.Highlights;
                }
            }
            Interlocked.Add(ref lowR, localLowR);
            Interlocked.Add(ref lowG, localLowG);
            Interlocked.Add(ref lowB, localLowB);
            Interlocked.Add(ref lowAll, localLowAll);
        });

        var divisor = pixels == 0 ? 1d : pixels;
        var stats = new ClippingStats(
            sourceSaturation?.High ?? ChannelClip.Empty,
            new ChannelClip(lowR / divisor, lowG / divisor, lowB / divisor),
            sourceSaturation?.HighAny ?? 0,
            lowAll / divisor,
            sourceSaturation != null);
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

    private static int WorkerCount(int pixelCount) =>
        Math.Min(Environment.ProcessorCount, Math.Max(1, pixelCount / 8192));

    private static (int Start, int End) ChunkRange(
        int pixelCount,
        int worker,
        int workers) =>
        ((int)((long)pixelCount * worker / workers),
            (int)((long)pixelCount * (worker + 1) / workers));

    private static ushort[] GetRgbSamples(MagickImage image) =>
        image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGB) ??
        throw new InvalidOperationException("Unable to read Q16 RGB pixels.");
}
