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
        var matrix = RgbColorSpaceMatrices.LinearRec2020ToLinearSrgb;
        var threshold = RawNearClipThreshold / (double)ushort.MaxValue;
        for (var i = 0; i < samples.Length; i += 3)
        {
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
                clipped++;
            }
        }

        return pixels == 0 ? 0 : (double)clipped / pixels;
    }

    public static ClippingAnalysis Analyze(
        MagickImage image,
        double rawNearClip,
        bool createOverlay,
        SceneHighlightAnalysis? sceneHighlights = null)
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
            var rHigh = sceneHighlights == null && r >= HighThreshold;
            var gHigh = sceneHighlights == null && g >= HighThreshold;
            var bHigh = sceneHighlights == null && b >= HighThreshold;
            var anyHigh = sceneHighlights == null
                ? rHigh || gHigh || bHigh
                : IsSceneHigh(sceneHighlights, pixel, image.Width, image.Height);
            var rLow = r <= LowThreshold;
            var gLow = g <= LowThreshold;
            var bLow = b <= LowThreshold;

            if (rHigh) highR++;
            if (gHigh) highG++;
            if (bHigh) highB++;
            if (rLow) lowR++;
            if (gLow) lowG++;
            if (bLow) lowB++;

            if (anyHigh)
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
            sceneHighlights?.High ??
                new ChannelClip(highR / divisor, highG / divisor, highB / divisor),
            new ChannelClip(lowR / divisor, lowG / divisor, lowB / divisor),
            sceneHighlights?.HighAny ?? highAny / divisor,
            lowAll / divisor,
            rawNearClip);
        return new ClippingAnalysis(
            stats,
            overlay == null
                ? null
                : CreateOverlay(overlay, image.Width, image.Height));
    }

    internal static SceneHighlightAnalysis AnalyzeSceneHighlights(
        MagickImage image,
        double[,] whiteBalanceMatrix,
        double exposureEv,
        bool createMask)
    {
        ArgumentNullException.ThrowIfNull(image);
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

        var samples = GetRgbSamples(image);
        var pixels = samples.Length / 3;
        var mask = createMask ? new bool[pixels] : null;
        long highR = 0, highG = 0, highB = 0, highAny = 0;
        var gain = Math.Pow(2, exposureEv);
        for (var pixel = 0; pixel < pixels; pixel++)
        {
            var offset = pixel * 3;
            var red = samples[offset] / (double)ushort.MaxValue;
            var green = samples[offset + 1] / (double)ushort.MaxValue;
            var blue = samples[offset + 2] / (double)ushort.MaxValue;
            var rHigh = gain * Transform(
                whiteBalanceMatrix, 0, red, green, blue) >= 1;
            var gHigh = gain * Transform(
                whiteBalanceMatrix, 1, red, green, blue) >= 1;
            var bHigh = gain * Transform(
                whiteBalanceMatrix, 2, red, green, blue) >= 1;
            if (rHigh) highR++;
            if (gHigh) highG++;
            if (bHigh) highB++;
            if (rHigh || gHigh || bHigh)
            {
                highAny++;
                if (mask != null) mask[pixel] = true;
            }
        }

        var divisor = Math.Max(1, pixels);
        return new SceneHighlightAnalysis(
            new ChannelClip(
                highR / (double)divisor,
                highG / (double)divisor,
                highB / (double)divisor),
            highAny / (double)divisor,
            image.Width,
            image.Height,
            mask);
    }

    private static ushort[] GetRgbSamples(MagickImage image) =>
        image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGB) ??
        throw new InvalidOperationException("Unable to read Q16 RGB pixels.");

    private static double Transform(
        double[,] matrix,
        int row,
        double red,
        double green,
        double blue) =>
        matrix[row, 0] * red +
        matrix[row, 1] * green +
        matrix[row, 2] * blue;

    private static bool IsSceneHigh(
        SceneHighlightAnalysis scene,
        int pixel,
        uint width,
        uint height)
    {
        if (scene.HighMask == null || width == 0 || height == 0 ||
            scene.Width == 0 || scene.Height == 0)
        {
            return false;
        }

        var x = pixel % checked((int)width);
        var y = pixel / checked((int)width);
        var sourceX = Math.Min(
            checked((int)scene.Width) - 1,
            (int)((x + 0.5) * scene.Width / width));
        var sourceY = Math.Min(
            checked((int)scene.Height) - 1,
            (int)((y + 0.5) * scene.Height / height));
        return scene.HighMask[sourceY * checked((int)scene.Width) + sourceX];
    }

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
