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
        RenderColorEncoding.EncodedFrame frame,
        int width,
        int height,
        SourceSaturationProjection? sourceSaturation,
        bool createOverlay,
        ClippingOverlaySide overlaySides = ClippingOverlaySide.Both,
        byte[]? bgra = null,
        bool countClipping = true)
    {
        var (samples, layout, alpha) = frame;
        var pixels = checked(width * height);
        if (sourceSaturation != null &&
            (sourceSaturation.Mask.Width != width ||
             sourceSaturation.Mask.Height != height))
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
                var sample = pixel * layout.Channels;
                var red = samples[sample + layout.Red];
                var green = samples[sample + layout.Green];
                var blue = samples[sample + layout.Blue];
                if (bgra != null)
                {
                    bgra[pixel * 4] = Scale(blue);
                    bgra[pixel * 4 + 1] = Scale(green);
                    bgra[pixel * 4 + 2] = Scale(red);
                    bgra[pixel * 4 + 3] = alpha is { } a ? Scale(samples[sample + a]) : (byte)255;
                }
                if (!countClipping) continue;
                var rLow = red <= LowThreshold;
                var gLow = green <= LowThreshold;
                var bLow = blue <= LowThreshold;
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
            countClipping ? stats : ClippingStats.Empty,
            flags == null
                ? null
                : new ClippingMask(
                    width,
                    height,
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

    // Magick Q16 to byte scaling: nearest integer, including alpha.
    private static byte Scale(ushort value) => (byte)((value + 128) / 257);
}
