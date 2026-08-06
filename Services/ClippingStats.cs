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

internal readonly record struct ClippingAnalysis(
    ClippingStats Stats,
    MagickImage? OverlayMask);

internal static class ClippingStatsCalculator
{
    private const ushort HighThreshold = 65407;
    private const ushort LowThreshold = 128;
    private const ushort RawNearClipThreshold = 64880;
    private const ushort OverlayAlpha = 24576;

    public static double CalculateRawNearClip(BaseImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!image.Info.IsRawSource)
        {
            return 0;
        }

        var samples = GetRgbSamples(image.Pixels);
        var pixels = samples.Length / 3;
        long clipped = 0;
        for (var i = 0; i < samples.Length; i += 3)
        {
            if (samples[i] >= RawNearClipThreshold ||
                samples[i + 1] >= RawNearClipThreshold ||
                samples[i + 2] >= RawNearClipThreshold)
            {
                clipped++;
            }
        }

        return pixels == 0 ? 0 : (double)clipped / pixels;
    }

    public static ClippingAnalysis Analyze(
        MagickImage image,
        double rawNearClip,
        bool createOverlay)
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

        var overlay = createOverlay ? new ushort[pixels * 4] : null;
        long highR = 0, highG = 0, highB = 0;
        long lowR = 0, lowG = 0, lowB = 0;
        long highAny = 0, lowAll = 0;

        for (var pixel = 0; pixel < pixels; pixel++)
        {
            var sample = pixel * 3;
            var r = samples[sample];
            var g = samples[sample + 1];
            var b = samples[sample + 2];
            var rHigh = r >= HighThreshold;
            var gHigh = g >= HighThreshold;
            var bHigh = b >= HighThreshold;
            var rLow = r <= LowThreshold;
            var gLow = g <= LowThreshold;
            var bLow = b <= LowThreshold;

            if (rHigh) highR++;
            if (gHigh) highG++;
            if (bHigh) highB++;
            if (rLow) lowR++;
            if (gLow) lowG++;
            if (bLow) lowB++;

            if (rHigh || gHigh || bHigh)
            {
                highAny++;
                SetOverlayPixel(overlay, pixel, Quantum.Max, 0, 0);
            }
            else if (rLow && gLow && bLow)
            {
                lowAll++;
                SetOverlayPixel(overlay, pixel, 0, 0, Quantum.Max);
            }
        }

        var divisor = (double)pixels;
        var stats = new ClippingStats(
            new ChannelClip(highR / divisor, highG / divisor, highB / divisor),
            new ChannelClip(lowR / divisor, lowG / divisor, lowB / divisor),
            highAny / divisor,
            lowAll / divisor,
            rawNearClip);
        return new ClippingAnalysis(
            stats,
            overlay == null
                ? null
                : CreateOverlay(overlay, image.Width, image.Height));
    }

    private static ushort[] GetRgbSamples(MagickImage image) =>
        image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGB) ??
        throw new InvalidOperationException("Unable to read Q16 RGB pixels.");

    private static void SetOverlayPixel(
        ushort[]? overlay,
        int pixel,
        ushort r,
        ushort g,
        ushort b)
    {
        if (overlay == null)
        {
            return;
        }

        var offset = pixel * 4;
        overlay[offset] = r;
        overlay[offset + 1] = g;
        overlay[offset + 2] = b;
        overlay[offset + 3] = OverlayAlpha;
    }

    private static MagickImage CreateOverlay(
        ushort[] pixels,
        uint width,
        uint height)
    {
        var image = new MagickImage(MagickColors.Transparent, width, height);
        try
        {
            var settings = new PixelImportSettings(
                width,
                height,
                StorageType.Short,
                PixelMapping.RGBA);
            image.ImportPixels(
                System.Runtime.InteropServices.MemoryMarshal.AsBytes(
                    pixels.AsSpan()),
                settings);
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }
}
