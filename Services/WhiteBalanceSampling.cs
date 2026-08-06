using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

public sealed record WhiteBalanceBaseContext(
    double AsShotKelvin,
    double AsShotTint,
    bool IsRawSource);

internal static class WhiteBalanceSampling
{
    private const double LowThreshold = 0.005;
    private const double PickHighThreshold = 0.95;
    private const double AutoHighThreshold = 0.98;

    public static double[]? PickGains(
        MagickImage image,
        EditSettings settings,
        double normalizedX,
        double normalizedY)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(settings);
        using var oriented = (MagickImage)image.Clone();
        RenderGeometry.Apply(oriented, settings);
        var centerX = (int)Math.Round(
            Math.Clamp(normalizedX, 0, 1) * (oriented.Width - 1));
        var centerY = (int)Math.Round(
            Math.Clamp(normalizedY, 0, 1) * (oriented.Height - 1));
        return CalculateGains(
            oriented,
            Math.Max(0, centerX - 2),
            Math.Min((int)oriented.Width - 1, centerX + 2),
            Math.Max(0, centerY - 2),
            Math.Min((int)oriented.Height - 1, centerY + 2),
            stepX: 1,
            stepY: 1,
            PickHighThreshold,
            rejectWholeSample: true);
    }

    public static double[]? AutoGains(MagickImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var stepX = Math.Max(1, (int)Math.Ceiling(image.Width / 64.0));
        var stepY = Math.Max(1, (int)Math.Ceiling(image.Height / 64.0));
        return CalculateGains(
            image,
            0,
            (int)image.Width - 1,
            0,
            (int)image.Height - 1,
            stepX,
            stepY,
            AutoHighThreshold,
            rejectWholeSample: false);
    }

    private static double[]? CalculateGains(
        MagickImage image,
        int left,
        int right,
        int top,
        int bottom,
        int stepX,
        int stepY,
        double highThreshold,
        bool rejectWholeSample)
    {
        double red = 0;
        double green = 0;
        double blue = 0;
        var count = 0;
        using var pixels = image.GetPixelsUnsafe();
        for (var y = top; y <= bottom; y += stepY)
        {
            for (var x = left; x <= right; x += stepX)
            {
                var pixel = pixels.GetPixel(x, y);
                var r = pixel[0] / (double)Quantum.Max;
                var g = pixel[1] / (double)Quantum.Max;
                var b = pixel[2] / (double)Quantum.Max;
                var invalid = r < LowThreshold || g < LowThreshold ||
                              b < LowThreshold || r > highThreshold ||
                              g > highThreshold || b > highThreshold;
                if (invalid && !rejectWholeSample)
                {
                    continue;
                }

                red += r;
                green += g;
                blue += b;
                count++;
            }
        }

        if (count == 0)
        {
            return null;
        }

        red /= count;
        green /= count;
        blue /= count;
        if (rejectWholeSample &&
            (red < LowThreshold || green < LowThreshold || blue < LowThreshold ||
             red > highThreshold || green > highThreshold || blue > highThreshold))
        {
            return null;
        }

        return
        [
            Math.Clamp(green / red, 0.2, 5),
            1,
            Math.Clamp(green / blue, 0.2, 5)
        ];
    }
}
